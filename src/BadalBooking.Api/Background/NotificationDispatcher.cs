using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Push;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Background;

public sealed record ClaimedNotification(Guid Id, string Type, Guid BookingId, Guid UserId, int Attempts);

public sealed record DispatchSummary(int Claimed, int Completed, int Retrying, int InvalidTokensRemoved);

// Sends queued notifications (outbox rows). Runs in the background, AFTER the booking change committed,
// never inside an HTTP request.
public sealed class NotificationDispatcher(
    AppDbContext db, IPushSender sender, TimeProvider clock, PushOptions options, ILogger<NotificationDispatcher> logger)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<DispatchSummary> DispatchDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = await ClaimAsync(now, ct);
        int completed = 0, retrying = 0, removed = 0;

        foreach (var n in claimed)
        {
            var (done, invalid) = await DeliverAsync(n, now, ct);
            if (done) completed++; else retrying++;
            removed += invalid;
        }

        return new DispatchSummary(claimed.Count, completed, retrying, removed);
    }

    // Take a batch of due rows and "lease" them for 2 minutes, so a second API instance skips them.
    // If this instance crashes mid-send, the lease runs out and another run picks the row up again.
    private Task<List<ClaimedNotification>> ClaimAsync(DateTime now, CancellationToken ct) =>
        db.Database.SqlQuery<ClaimedNotification>($"""
            UPDATE app.notification_outbox AS o
               SET locked_until_utc = {now + Lease}, attempts = o.attempts + 1
             WHERE o.id IN (
                   SELECT id FROM app.notification_outbox
                    WHERE processed_at_utc IS NULL
                      AND next_attempt_at_utc <= {now}
                      AND (locked_until_utc IS NULL OR locked_until_utc <= {now})
                    ORDER BY created_at_utc
                    LIMIT {options.BatchSize}
                    FOR UPDATE SKIP LOCKED)
            RETURNING o.id, o.type, o.booking_id, o.user_id, o.attempts
            """).ToListAsync(ct);

    private async Task<(bool Done, int InvalidRemoved)> DeliverAsync(ClaimedNotification n, DateTime now, CancellationToken ct)
    {
        // Devices are looked up NOW, not when the event was queued: a device removed by logout gets nothing.
        var tokens = await db.DeviceTokens.Where(d => d.UserId == n.UserId).Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0)
        {
            await CompleteAsync(n.Id, now, "no registered devices", ct);
            return (true, 0);
        }

        var results = await sender.SendAsync(tokens, BuildMessage(n), ct);

        // Remove only tokens FCM rejected for good, and only if they still belong to this user
        // (the device may have been re-registered to someone else in the meantime).
        var invalid = results.Where(r => r.Outcome == PushOutcome.InvalidToken).Select(r => r.Token).ToList();
        var removed = invalid.Count == 0 ? 0
            : await db.DeviceTokens.Where(d => d.UserId == n.UserId && invalid.Contains(d.Token)).ExecuteDeleteAsync(ct);

        var temporary = results.Where(r => r.Outcome == PushOutcome.TemporaryFailure).ToList();
        if (temporary.Count == 0)
        {
            await CompleteAsync(n.Id, now, null, ct);
            logger.LogInformation("Push {Type} for booking {BookingId}: sent to {Sent} device(s), removed {Removed} invalid token(s).",
                n.Type, n.BookingId, results.Count(r => r.Outcome == PushOutcome.Sent), removed);
            return (true, removed);
        }

        var error = string.Join(",", temporary.Select(t => t.Error).Distinct());
        if (n.Attempts >= options.MaxAttempts)
        {
            await CompleteAsync(n.Id, now, $"gave up after {n.Attempts} attempts: {error}", ct);
            logger.LogWarning("Push {Type} for booking {BookingId} gave up after {Attempts} attempts.", n.Type, n.BookingId, n.Attempts);
            return (true, removed);
        }

        // Retry with exponential backoff: 30s, 1m, 2m, 4m ... capped at 1 hour.
        // Delivery is "at least once": devices that already got it may get it again on retry,
        // so the message carries an eventId the app can use to ignore duplicates.
        var delay = TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, n.Attempts - 1)));
        await db.Outbox.Where(o => o.Id == n.Id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.NextAttemptAtUtc, now + delay)
            .SetProperty(o => o.LockedUntilUtc, (DateTime?)null)
            .SetProperty(o => o.LastError, error), ct);
        logger.LogWarning("Push {Type} for booking {BookingId} will retry in {Delay}: {Error}", n.Type, n.BookingId, delay, error);
        return (false, removed);
    }

    private Task CompleteAsync(Guid id, DateTime now, string? note, CancellationToken ct) =>
        db.Outbox.Where(o => o.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.ProcessedAtUtc, now)
            .SetProperty(o => o.LockedUntilUtc, (DateTime?)null)
            .SetProperty(o => o.LastError, note), ct);

    // Lock-screen text stays generic: no pilgrim names or other personal data.
    private static PushMessage BuildMessage(ClaimedNotification n)
    {
        var (title, body) = n.Type switch
        {
            NotificationTypes.BookingAccepted => ("Booking accepted", "A performer has accepted your Umrah booking."),
            NotificationTypes.BookingExpired => ("Booking expired", "Your booking expired because it wasn't paid within 30 minutes."),
            _ => ("Booking update", "Your booking was updated."),
        };
        return new PushMessage(title, body, new Dictionary<string, string>
        {
            ["eventId"] = n.Id.ToString(),
            ["type"] = n.Type,
            ["bookingId"] = n.BookingId.ToString(),
        });
    }
}
