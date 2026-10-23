using BadalBooking.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Background;

public sealed record ExpiredBooking(Guid Id, Guid CustomerId);

// The actual expiry rule, separate from the timer loop so tests can call it with a fake clock.
public sealed class BookingExpiryService(AppDbContext db, TimeProvider clock, ExpiryOptions options)
{
    // Expires every booking that is STILL PendingPayment and whose 30-minute deadline has passed.
    // Returns the expired bookings (step 2.5 uses them to queue "booking expired" push notifications).
    public async Task<IReadOnlyList<ExpiredBooking>> ExpireDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var all = new List<ExpiredBooking>();

        while (!ct.IsCancellationRequested)
        {
            var batch = await ExpireBatchAsync(now, ct);
            all.AddRange(batch);
            if (batch.Count < options.BatchSize) break; // nothing left
        }

        return all;
    }

    // One SQL statement does find + change, so nothing can slip in between:
    //  - status = 'PendingPayment' : a booking paid or cancelled a moment ago is never touched.
    //    (The pay endpoint uses the opposite condition, deadline > now, so pay and expire can't both win.)
    //  - payment_deadline_utc <= now : the same stored deadline the pay endpoint checks.
    //  - FOR UPDATE SKIP LOCKED : if a second API instance (or a payment) is working on a row right now,
    //    skip it instead of waiting; it will be re-checked on the next run.
    //  - LIMIT : small batches keep locks short.
    //  - RETURNING : tells us exactly which rows THIS run expired.
    private async Task<List<ExpiredBooking>> ExpireBatchAsync(DateTime now, CancellationToken ct)
    {
        var batchSize = options.BatchSize;

        return await db.Database.SqlQuery<ExpiredBooking>($"""
            UPDATE app.bookings AS b
               SET status = 'Expired', updated_at_utc = {now}
             WHERE b.id IN (
                   SELECT id FROM app.bookings
                    WHERE status = 'PendingPayment'
                      AND payment_deadline_utc <= {now}
                    ORDER BY payment_deadline_utc
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
               AND b.status = 'PendingPayment'
            RETURNING b.id, b.customer_id
            """).ToListAsync(ct);
        // Column names stay snake_case (id, customer_id): EF's naming convention maps them to
        // ExpiredBooking.Id / CustomerId. Renaming them to "Id"/"CustomerId" made EF fail to read
        // the result AFTER the UPDATE had already run (caught in the step 1.7 demo).
    }
}
