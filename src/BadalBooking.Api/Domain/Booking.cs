namespace BadalBooking.Api.Domain;

public class Booking
{
    // Business rules live in one place. The client never sends a price.
    public const int PriceHalalas = 50_000; // 500.00 SAR in halalas (1 SAR = 100 halalas)
    public const string PriceCurrency = "SAR";
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(30);

    public const int PilgrimNameMaxLength = 200;

    // EF Core uses this to load rows. Application code must use Booking.Create.
    private Booking() { }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? PerformerId { get; private set; }
    public string PilgrimName { get; private set; } = "";

    // Integer minor units, never double/decimal rounding surprises.
    public int AmountHalalas { get; private set; }
    public string Currency { get; private set; } = "";

    public BookingStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    // Stored once at creation (CreatedAtUtc + 30 min). The expiry job and the pay
    // endpoint both compare against this same value, so they can never disagree.
    public DateTime PaymentDeadlineUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    // Concurrency token. Mapped to PostgreSQL's built-in xmin column (no extra column).
    public uint Version { get; private set; }

    public static Booking Create(Guid customerId, string pilgrimName, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        CustomerId = customerId,
        PilgrimName = pilgrimName.Trim(),
        AmountHalalas = PriceHalalas,
        Currency = PriceCurrency,
        Status = BookingStatus.PendingPayment,
        CreatedAtUtc = nowUtc,
        PaymentDeadlineUtc = nowUtc + PaymentWindow,
        UpdatedAtUtc = nowUtc,
    };
}
