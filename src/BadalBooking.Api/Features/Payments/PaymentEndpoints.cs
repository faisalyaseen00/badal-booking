using System.Security.Claims;
using System.Text.RegularExpressions;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
using BadalBooking.Api.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BadalBooking.Api.Features.Payments;

public static partial class PaymentEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/bookings/{id:guid}/pay", Pay)
            .WithTags("Payments")
            .RequireAuthorization(AuthClaims.CustomerPolicy);
        return app;
    }

    // POST /bookings/{id}/pay   Header: Idempotency-Key: <unique per payment attempt, e.g. a UUID>
    //
    // Guarantees:
    //  1. Same key again (double-click, network retry)  -> same stored payment returned, no second charge.
    //  2. Different keys at the same time                -> exactly one wins, the others get 409.
    //  3. Only the owner can pay, only while PendingPayment, only before the 30-minute deadline.
    private static async Task<IResult> Pay(
        Guid id, HttpRequest request, HttpResponse response, ClaimsPrincipal principal,
        AppDbContext db, IPaymentGateway gateway, TimeProvider clock, CancellationToken ct)
    {
        var key = request.Headers[IdempotencyHeader].ToString().Trim();
        if (!IsValidKey(key))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [IdempotencyHeader] = [$"Header is required: 1 to {Payment.IdempotencyKeyMaxLength} characters (letters, digits, '-', '_'). A UUID is recommended."],
            });

        var customerId = principal.GetUserId();

        // 1. Seen this key before? Return the original result instead of charging again.
        var replay = await TryReplayAsync(db, response, customerId, key, id, ct);
        if (replay is not null) return replay;

        // 2. Load the booking: only the caller's own (someone else's -> 404, same as missing).
        var booking = await db.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id && b.CustomerId == customerId, ct);
        if (booking is null) return Results.NotFound();

        var now = clock.GetUtcNow().UtcDateTime;
        if (booking.Status != BookingStatus.PendingPayment)
            // Look for the key again before saying "conflict": a request with the SAME key may have
            // committed between step 1 and now. (Found by the parallel same-key demo: without this,
            // some retries got 409 instead of the original payment.)
            return await TryReplayAsync(db, response, customerId, key, id, ct)
                ?? Conflict($"Booking cannot be paid because it is {booking.Status}.");
        // Checked here too, not only by the expiry job: the job may not have run yet.
        if (now >= booking.PaymentDeadlineUtc)
            return Conflict("The 30-minute payment window has expired.");

        // 3. Claim the booking atomically. The WHERE clause re-checks owner, status and deadline
        //    inside the database. If two requests race, PostgreSQL row locking lets only ONE of
        //    them change the row; the other sees 0 rows updated.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await db.Bookings
            .Where(b => b.Id == id
                     && b.CustomerId == customerId
                     && b.Status == BookingStatus.PendingPayment
                     && b.PaymentDeadlineUtc > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(b => b.Status, BookingStatus.Paid)
                .SetProperty(b => b.UpdatedAtUtc, now), ct);

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            // Lost the race. If the winner used the SAME key, this is a retry: return its result.
            return await TryReplayAsync(db, response, customerId, key, id, ct)
                ?? Conflict("Booking was already paid or changed by another request.");
        }

        // 4. Charge. Only the request that won step 3 ever reaches the gateway.
        //    (Fine for the instant simulator. A real gateway call should not happen inside a
        //    database transaction; see the note in IPaymentGateway.)
        var charge = await gateway.ChargeAsync(
            new ChargeRequest(booking.Id, booking.AmountHalalas, booking.Currency, key), ct);

        if (!charge.Succeeded)
        {
            await tx.RollbackAsync(ct); // booking goes back to PendingPayment
            return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired, title: "Payment was declined.");
        }

        // 5. Record the payment in the SAME transaction as the status change:
        //    either both are saved or neither is.
        var payment = Payment.Succeeded(booking, key, charge.Provider, charge.ProviderReference, now);
        db.Payments.Add(payment);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Backstop: the unique indexes caught a duplicate the checks above missed.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await TryReplayAsync(db, response, customerId, key, id, ct)
                ?? Conflict("Booking was already paid.");
        }

        return Results.Ok(PaymentDto.From(payment));
    }

    private static async Task<IResult?> TryReplayAsync(
        AppDbContext db, HttpResponse response, Guid customerId, string key, Guid bookingId, CancellationToken ct)
    {
        var existing = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.CustomerId == customerId && p.IdempotencyKey == key, ct);
        if (existing is null) return null;

        // Same key, different booking: the client has a bug. Refuse instead of guessing.
        if (existing.BookingId != bookingId)
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "This Idempotency-Key was already used for a different booking.");

        response.Headers[ReplayedHeader] = "true";
        return Results.Ok(PaymentDto.From(existing));
    }

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static bool IsValidKey(string key) =>
        key.Length is > 0 and <= Payment.IdempotencyKeyMaxLength && KeyFormat().IsMatch(key);

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex KeyFormat();
}
