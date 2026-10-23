using System.Threading.RateLimiting;
using BadalBooking.Api.Background;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Features.Auth;
using BadalBooking.Api.Features.Bookings;
using BadalBooking.Api.Features.Devices;
using BadalBooking.Api.Features.Health;
using BadalBooking.Api.Features.Status;
using BadalBooking.Api.Features.PerformerApplications;
using BadalBooking.Api.Features.Payments;
using BadalBooking.Api.Features.Performers;
using BadalBooking.Api.Infrastructure.Payments;
using BadalBooking.Api.Infrastructure;
using BadalBooking.Api.Infrastructure.Auth;
using BadalBooking.Api.Infrastructure.Firebase;
using BadalBooking.Api.Infrastructure.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Local secrets live in the git-ignored .env file at the repo root.
// Only loaded in Development; in production every value comes from real environment variables.
if (builder.Environment.IsDevelopment())
{
    DotEnv.LoadForDevelopment(builder.Environment.ContentRootPath);
    builder.Configuration.AddEnvironmentVariables(); // re-read so the .env values are visible
}

// ---------- Hosting: Render (backend) + Vercel (frontend) ----------
builder.UsePlatformPort();          // listen on Render's PORT
builder.AddPlatformProxySupport();  // real client IP behind Render's proxy
builder.AddStrictCors();            // only the exact Vercel/localhost origins may call from a browser

// ---------- Database ----------
// Comes from .env locally, and from the ConnectionStrings__Default
// environment variable in production (Render). Never from a committed file.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing. Set ConnectionStrings__Default.");

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        // Keep EF's own bookkeeping table in our "app" schema too, not in "public".
        npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
    .UseSnakeCaseNamingConvention()); // PascalCase C# names -> snake_case SQL names (no quoting needed)

// ---------- Core services ----------
// Injected clock instead of DateTime.UtcNow, so tests can move time (needed for the 30-minute expiry).
builder.Services.AddSingleton(TimeProvider.System);
// ASP.NET Core's built-in hasher: PBKDF2 with a random salt per password.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
// Consistent RFC 7807 JSON error bodies for 400/401/403/404/409/429/500.
builder.Services.AddProblemDetails();

// ---------- Payments ----------
// Only the simulator exists today. Any other value stops startup, so production can never
// silently run with a gateway that isn't really there.
var paymentProvider = builder.Configuration["Payments:Provider"] ?? SimulatedPaymentGateway.ProviderName;
if (paymentProvider != SimulatedPaymentGateway.ProviderName)
    throw new InvalidOperationException($"Payments__Provider '{paymentProvider}' is not supported. Use '{SimulatedPaymentGateway.ProviderName}'.");
builder.Services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

// ---------- Firebase Admin SDK (sign-in + push, Phase 2) ----------
builder.AddFirebase(); // key comes only from Firebase__ServiceAccountJson
builder.Services.AddScoped<FirebaseSignInService>();

// ---------- Background job: send queued push notifications (outbox) ----------
var push = PushOptions.LoadAndValidate(builder.Configuration);
builder.Services.AddSingleton(push);
builder.Services.AddScoped<NotificationDispatcher>();
// Only when a sender exists (Firebase configured, or a fake in tests). Without one, events stay queued.
if (push.Enabled && builder.Services.Any(s => s.ServiceType == typeof(BadalBooking.Api.Infrastructure.Push.IPushSender)))
    builder.Services.AddHostedService<PushWorker>();

// ---------- Background job: expire bookings unpaid for 30 minutes ----------
var expiry = ExpiryOptions.LoadAndValidate(builder.Configuration);
builder.Services.AddSingleton(expiry);
builder.Services.AddScoped<BookingExpiryService>();
if (expiry.Enabled) builder.Services.AddHostedService<ExpiryWorker>();

// ---------- Authentication: the API's own JWT ----------
var jwt = JwtOptions.LoadAndValidate(builder.Configuration); // stops startup if Jwt__Key is missing/weak
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep "sub" and "role" exactly as issued
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.KeyBytes),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // reject "alg: none" and algorithm swaps
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AuthClaims.UserId,
            RoleClaimType = AuthClaims.Role,
        };
    });

// Check token expiry with the SAME clock that issued the token (the injected TimeProvider),
// instead of the system clock. One clock for the whole app; tests can move it safely.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((options, clock) =>
        options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var skew = parameters.ClockSkew;
            return expires is not null
                && now - skew < expires.Value
                && (notBefore is null || notBefore.Value <= now + skew);
        });

// Role checks used by the booking endpoints (step 1.4 onwards).
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthClaims.CustomerPolicy, p => p.RequireRole(nameof(UserRole.Customer)))
    .AddPolicy(AuthClaims.PerformerPolicy, p => p.RequireRole(nameof(UserRole.Performer)))
    .AddPolicy(AuthClaims.AdminPolicy, p => p.RequireRole(nameof(UserRole.Admin)));

// ---------- Rate limiting for login/register (brute-force protection) ----------
var authPermitsPerMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown", // per client IP
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// ---------- HTTP pipeline (order matters) ----------
app.UsePlatformProxySupport(); // FIRST: fix client IP/scheme before anything reads them
app.UseExceptionHandler();   // unhandled errors -> generic 500 ProblemDetails, no stack trace to clients
app.UseStatusCodePages();    // empty 401/403/404 -> ProblemDetails body
app.UseCors(HostingSetup.CorsPolicy); // before auth, so browser preflight (OPTIONS) gets CORS headers
app.UseAuthentication();     // reads and validates the Bearer token
app.UseAuthorization();      // applies RequireAuthorization / policies
app.UseRateLimiter();

// ---------- Endpoints ----------
// /ping proves the process is up; /health also checks the database.
app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));
app.MapHealthEndpoints();
app.MapStatusEndpoints();
app.MapAuthEndpoints();
app.MapBookingEndpoints();
app.MapPaymentEndpoints();
app.MapPerformerEndpoints();
app.MapDeviceEndpoints();
app.MapPerformerApplicationEndpoints();

// ---------- Database migrations on startup ----------
// Every deploy brings the database schema up to date BEFORE the API serves requests or the
// background workers start. EF Core takes a database lock while migrating, so two instances
// starting together can't both run the same migration. If a migration fails, startup fails,
// and Render keeps the previous version running instead of serving a half-migrated app.
// (Found the need for this in step 2.10: a not-yet-migrated database crashed the API.)
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count > 0)
    {
        app.Logger.LogInformation("Applying {Count} database migration(s): {Names}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
    }
}

// Create the admin (and optional demo performer) if they don't exist yet. Neither can self-register.
await AccountSeeder.SeedAsync(app.Services);

app.Logger.LogWarning("Payments are SIMULATED: no real money is charged (Payments__Provider={Provider}).", paymentProvider);

app.Run();

// Makes the auto-generated Program class visible to the test project,
// so tests can start the whole API in memory with WebApplicationFactory.
public partial class Program { }
