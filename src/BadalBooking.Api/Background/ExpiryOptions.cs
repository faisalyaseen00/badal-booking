namespace BadalBooking.Api.Background;

// Not secret, so the defaults live in appsettings.json. Override with Expiry__IntervalSeconds etc.
public sealed class ExpiryOptions
{
    public const string SectionName = "Expiry";

    public bool Enabled { get; init; } = true;       // tests turn the worker off and call the service directly
    public int IntervalSeconds { get; init; } = 60;  // how often to look for overdue bookings
    public int BatchSize { get; init; } = 100;       // rows per UPDATE, so one run never locks the whole table

    public static ExpiryOptions LoadAndValidate(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<ExpiryOptions>() ?? new ExpiryOptions();
        if (options.IntervalSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Expiry__IntervalSeconds must be between 1 and 3600.");
        if (options.BatchSize is < 1 or > 1000)
            throw new InvalidOperationException("Expiry__BatchSize must be between 1 and 1000.");
        return options;
    }
}
