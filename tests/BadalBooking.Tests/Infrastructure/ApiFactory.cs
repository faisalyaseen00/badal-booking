using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace BadalBooking.Tests.Infrastructure;

// Runs the REAL API in memory against a REAL, throwaway PostgreSQL 17 container.
// - Same database engine as production, so row locks, unique indexes and check constraints
//   behave exactly like they will on Supabase. (An in-memory fake DB would hide race bugs.)
// - Never touches your local "badal" database or .env: environment is "Testing",
//   and every setting is passed in below.
// - One container for the whole test run (shared via TestCollection), deleted afterwards.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PerformerEmail = "seeded.performer@test.local";
    public const string PerformerPassword = "Performer-Test-Password-1";
    public const string TestPassword = "Secret123!";
    public const string AdminEmail = "seeded.admin@test.local";
    public const string AdminPassword = "Admin-Test-Password-1";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("badal_test")
        .Build();

    // A clock the tests control. Starts "now" and only moves when a test calls Advance().
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    // Fake FCM: records sends; tests choose per-token outcomes.
    public FakePushSender Push { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Create the schema with the SAME migrations production uses.
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); // not Development: .env is NOT loaded

        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-at-least-32-characters-long");
        builder.UseSetting("Jwt:Issuer", "BadalBooking");
        builder.UseSetting("Jwt:Audience", "BadalBooking.Clients");
        builder.UseSetting("Seed:PerformerEmail", PerformerEmail);
        builder.UseSetting("Seed:PerformerPassword", PerformerPassword);
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Cors:AllowedOrigins:0", "https://status.test.example");
        builder.UseSetting("Expiry:Enabled", "false");            // tests run expiry explicitly
        builder.UseSetting("Push:Enabled", "false");              // tests run the push dispatcher explicitly
        builder.UseSetting("RateLimits:AuthPerMinute", "100000"); // tests log in a lot

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<BadalBooking.Api.Infrastructure.Firebase.IFirebaseTokenVerifier>();
            services.AddSingleton<BadalBooking.Api.Infrastructure.Firebase.IFirebaseTokenVerifier, FakeFirebaseVerifier>();
            services.RemoveAll<BadalBooking.Api.Infrastructure.Push.IPushSender>();
            services.AddSingleton<BadalBooking.Api.Infrastructure.Push.IPushSender>(Push);
        });
    }

    public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(),
            o => o.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
        .UseSnakeCaseNamingConvention()
        .Options);

    // Test-only shortcut for race tests that need many performers. The real app has no way to do this.
    public async Task<string> CreatePerformerAsync()
    {
        var email = $"performer-{Guid.NewGuid():N}@test.local";
        await using var db = CreateDbContext();
        var performer = User.CreatePerformer(email, Clock.GetUtcNow().UtcDateTime);
        performer.SetPasswordHash(new PasswordHasher<User>().HashPassword(performer, TestPassword));
        db.Users.Add(performer);
        await db.SaveChangesAsync();
        return email;
    }
}

[CollectionDefinition(Name)]
public sealed class TestCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
