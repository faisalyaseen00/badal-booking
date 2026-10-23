using Microsoft.AspNetCore.HttpOverrides;

namespace BadalBooking.Api.Infrastructure.Hosting;

// Everything that must be right for the API to work on Render, with a frontend on Vercel.
public static class HostingSetup
{
    public const string CorsPolicy = "Frontends";

    // Render sets RENDER=true on every service automatically.
    public static bool IsOnRender(this IConfiguration config) => config.GetValue<bool>("RENDER");

    // Render tells the app which port to listen on through the PORT variable.
    // Locally PORT is not set, so launchSettings.json (5080) is used.
    public static void UsePlatformPort(this WebApplicationBuilder builder)
    {
        var port = builder.Configuration["PORT"];
        if (!string.IsNullOrWhiteSpace(port))
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    }

    // On Render, requests reach us through Render's load balancer, so the connection IP is the
    // proxy's, not the user's. Without this, the login rate limit would count ALL users as one
    // and lock everyone out. Only enabled on Render: anywhere else a client could fake the header.
    public static void AddPlatformProxySupport(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.IsOnRender()) return;

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1; // trust only the hop added by Render's proxy
            options.KnownIPNetworks.Clear(); // Render's proxy IPs aren't published, so accept the header from it
            options.KnownProxies.Clear();
        });
    }

    public static void UsePlatformProxySupport(this WebApplication app)
    {
        if (app.Configuration.IsOnRender()) app.UseForwardedHeaders();
    }

    // Browsers (the Vercel pages) may only call the API from these exact origins.
    // Cors__AllowedOrigins__0, __1, ... e.g. https://badal-booking-status.vercel.app
    public static void AddStrictCors(this WebApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var isDevelopment = builder.Environment.IsDevelopment();

        foreach (var origin in origins)
        {
            var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && !origin.Contains('*')
                && uri.AbsolutePath == "/" && !origin.EndsWith('/') // an origin has no path or trailing slash
                && (uri.Scheme == Uri.UriSchemeHttps || (isDevelopment && uri.Scheme == Uri.UriSchemeHttp));
            if (!valid)
                throw new InvalidOperationException(
                    $"Invalid CORS origin '{origin}'. Use an exact origin like https://your-app.vercel.app (no '*', no path, https outside Development).");
        }

        builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(origins) // empty list = no browser origin allowed at all
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
            .WithMethods("GET", "POST", "DELETE")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
        // No AllowCredentials(): the frontend sends the JWT in the Authorization header, not cookies.
    }
}
