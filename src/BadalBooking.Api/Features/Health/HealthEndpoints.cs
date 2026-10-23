using BadalBooking.Api.Data;

namespace BadalBooking.Api.Features.Health;

public sealed record HealthResponse(string Status, string Database, string Version, DateTime CheckedAtUtc);

public static class HealthEndpoints
{
    // Render (and the Vercel status page) call this. It must answer quickly and never leak details.
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(3);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /health — 200 when the API AND the database work, 503 when the database doesn't.
        // (/ping only proves the process is alive.)
        app.MapGet("/health", async (AppDbContext db, IConfiguration config, TimeProvider clock, CancellationToken ct) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DatabaseTimeout);

            bool databaseUp;
            try { databaseUp = await db.Database.CanConnectAsync(timeout.Token); }
            catch { databaseUp = false; } // no exception text in the response: it could contain host names

            // Render sets RENDER_GIT_COMMIT, so we can confirm which commit is live after a deploy.
            var version = config["RENDER_GIT_COMMIT"] is { Length: >= 7 } sha ? sha[..7] : "local";
            var body = new HealthResponse(databaseUp ? "Healthy" : "Unhealthy", databaseUp ? "Up" : "Down",
                version, clock.GetUtcNow().UtcDateTime);

            return databaseUp ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).WithTags("Health");

        return app;
    }
}
