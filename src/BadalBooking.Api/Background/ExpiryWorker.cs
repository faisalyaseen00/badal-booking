namespace BadalBooking.Api.Background;

// Runs inside the API process: once immediately at startup, then every IntervalSeconds.
//
// Why "immediately at startup": Render's free plan puts the service to sleep when idle, and the
// timer can't run while it sleeps. On wake-up the first run catches up on everything that became
// overdue in the meantime. The pay endpoint ALSO checks the deadline, so an overdue booking can
// never be paid even in the seconds before this job runs.
public sealed class ExpiryWorker(
    IServiceScopeFactory scopes, ExpiryOptions options, TimeProvider clock, ILogger<ExpiryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Expiry worker started: every {Seconds}s, batches of {Batch}.",
            options.IntervalSeconds, options.BatchSize);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervalSeconds), clock);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            // A NEW scope (and DbContext) per run. A DbContext is not thread-safe and must never be
            // shared with HTTP requests or kept alive for the whole lifetime of the worker.
            await using var scope = scopes.CreateAsyncScope();
            var expiry = scope.ServiceProvider.GetRequiredService<BookingExpiryService>();

            var expired = await expiry.ExpireDueAsync(ct);
            if (expired.Count > 0)
                logger.LogInformation("Expired {Count} unpaid booking(s).", expired.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // app is shutting down
        }
        catch (Exception ex)
        {
            // One failed run (e.g. database briefly unreachable) must not kill the worker for good.
            logger.LogError(ex, "Expiry run failed; will retry on the next tick.");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
