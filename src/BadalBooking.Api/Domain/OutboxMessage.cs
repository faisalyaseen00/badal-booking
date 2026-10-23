namespace BadalBooking.Api.Domain;

public static class NotificationTypes
{
    public const string BookingAccepted = "BookingAccepted";
    public const string BookingExpired = "BookingExpired";
}

// A push notification waiting to be sent: the "outbox" pattern.
//
// The row is written in the SAME database statement as the booking change, so:
//  - booking change saved  -> notification guaranteed to be queued (even if the app crashes next second)
//  - booking change failed -> no notification (never "your booking was accepted" for a booking that wasn't)
// A background worker (step 2.6) sends it AFTER commit, outside the HTTP request, and retries on failure.
public class OutboxMessage
{
    private OutboxMessage() { } // rows are created by SQL in the accept/expiry statements

    public Guid Id { get; private set; }
    public string Type { get; private set; } = "";
    public Guid BookingId { get; private set; }
    public Guid UserId { get; private set; }          // the customer to notify
    public DateTime CreatedAtUtc { get; private set; }

    // Delivery bookkeeping, used by the worker in step 2.6.
    public int Attempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public string? LastError { get; private set; }
}
