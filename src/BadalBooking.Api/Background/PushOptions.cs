namespace BadalBooking.Api.Background;

public sealed class PushOptions
{
    public const string SectionName = "Push";

    public bool Enabled { get; init; } = true;       // tests turn the loop off and call the dispatcher directly
    public int IntervalSeconds { get; init; } = 5;   // how often to look for queued notifications
    public int BatchSize { get; init; } = 20;
    public int MaxAttempts { get; init; } = 8;       // then give up, but keep the row (with last_error) for investigation

    public static PushOptions LoadAndValidate(IConfiguration configuration)
    {
        var o = configuration.GetSection(SectionName).Get<PushOptions>() ?? new PushOptions();
        if (o.IntervalSeconds is < 1 or > 600) throw new InvalidOperationException("Push__IntervalSeconds must be 1-600.");
        if (o.BatchSize is < 1 or > 500) throw new InvalidOperationException("Push__BatchSize must be 1-500.");
        if (o.MaxAttempts is < 1 or > 50) throw new InvalidOperationException("Push__MaxAttempts must be 1-50.");
        return o;
    }
}
