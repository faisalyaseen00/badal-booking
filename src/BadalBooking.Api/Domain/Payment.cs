namespace BadalBooking.Api.Domain;

public enum PaymentStatus
{
    Succeeded,
    Failed,   // reserved for a real gateway; the simulator always succeeds
    Refunded, // the customer cancelled a paid booking before a performer accepted it
}

// One row per payment attempt that reached the gateway.
// Database rules (AppDbContext): one key per customer, and at most ONE Succeeded payment per booking.
public class Payment
{
    public const int IdempotencyKeyMaxLength = 100;

    private Payment() { } // for EF Core

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid CustomerId { get; private set; }

    // Sent by the client in the "Idempotency-Key" header. Same key = same payment, never a second charge.
    public string IdempotencyKey { get; private set; } = "";

    public int AmountHalalas { get; private set; }
    public string Currency { get; private set; } = "";
    public PaymentStatus Status { get; private set; }

    public string Provider { get; private set; } = "";          // "Simulated" (later: "HyperPay")
    public string ProviderReference { get; private set; } = ""; // the gateway's transaction id

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }
    public string? RefundReference { get; private set; }

    public void MarkRefunded(string refundReference, DateTime nowUtc)
    {
        if (Status != PaymentStatus.Succeeded)
            throw new InvalidOperationException($"Only a Succeeded payment can be refunded (was {Status}).");
        Status = PaymentStatus.Refunded;
        RefundReference = refundReference;
        RefundedAtUtc = nowUtc;
    }

    // Amount and currency are copied from the BOOKING (which got them from the business rule),
    // never from the request.
    public static Payment Succeeded(Booking booking, string idempotencyKey,
        string provider, string providerReference, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        BookingId = booking.Id,
        CustomerId = booking.CustomerId,
        IdempotencyKey = idempotencyKey,
        AmountHalalas = booking.AmountHalalas,
        Currency = booking.Currency,
        Status = PaymentStatus.Succeeded,
        Provider = provider,
        ProviderReference = providerReference,
        CreatedAtUtc = nowUtc,
    };
}
