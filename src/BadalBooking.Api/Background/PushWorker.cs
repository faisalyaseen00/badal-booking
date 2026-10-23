namespace BadalBooking.Api.Background;

// Background loop: every few seconds, send queued notifications. Same shape as ExpiryWorker:
// runs at startup (catch-up after Render's free-tier sleep), new scope per run, errors logged and retried.
public sealed class PushWorker(
    IServiceScopeFactory scopes, PushOptions options, TimeProvider clock, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Push worker started: every {Seconds}s.", options.IntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervalSeconds), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                // Keep going while full batches come back, then wait for the next tick.
                DispatchSummary summary;
                do { summary = await dispatcher.DispatchDueAsync(stoppingToken); }
                while (summary.Claimed == options.BatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Push run failed; will retry on the next tick.");
            }
        }
        while (await NextTick(timer, stoppingToken));
    }

    private static async Task<bool> NextTick(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
