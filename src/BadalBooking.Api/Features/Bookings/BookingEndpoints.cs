using System.Security.Claims;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
using BadalBooking.Api.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Features.Bookings;

public static class BookingEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route in this group needs a valid JWT with role = Customer.
        // No token -> 401. Performer token -> 403.
        var customer = app.MapGroup("/bookings")
            .WithTags("Bookings")
            .RequireAuthorization(AuthClaims.CustomerPolicy);

        customer.MapPost("/", Create);
        customer.MapGet("/", ListMine);
        customer.MapGet("/{id:guid}", GetMine);
        customer.MapPost("/{id:guid}/cancel", Cancel);

        return app;
    }

    // POST /bookings/{id}/cancel — allowed only BEFORE a performer accepts.
    //   PendingPayment -> Cancelled                       (nothing to refund)
    //   Paid           -> Cancelled + simulated refund    (in one transaction)
    //   Accepted / Cancelled / Expired -> 409
    // Each change is a conditional UPDATE, so cancel and a performer's accept can never both win:
    // PostgreSQL locks the row, and whichever commits second finds the status already changed.
    private static async Task<IResult> Cancel(
        Guid id, ClaimsPrincipal principal, AppDbContext db, IPaymentGateway gateway,
        TimeProvider clock, CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Case 1: not paid yet. Nothing to refund.
        var cancelledUnpaid = await db.Bookings
            .Where(b => b.Id == id && b.CustomerId == customerId && b.Status == BookingStatus.PendingPayment)
            .ExecuteUpdateAsync(set => set
                .SetProperty(b => b.Status, BookingStatus.Cancelled)
                .SetProperty(b => b.UpdatedAtUtc, now), ct);

        var refunded = false;
        if (cancelledUnpaid == 0)
        {
            // Case 2: paid, and no performer has accepted it yet.
            var cancelledPaid = await db.Bookings
                .Where(b => b.Id == id && b.CustomerId == customerId
                         && b.Status == BookingStatus.Paid && b.PerformerId == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, BookingStatus.Cancelled)
                    .SetProperty(b => b.UpdatedAtUtc, now), ct);

            if (cancelledPaid == 0)
            {
                await tx.RollbackAsync(ct);
                var current = await db.Bookings.AsNoTracking()
                    .Where(b => b.Id == id && b.CustomerId == customerId)
                    .Select(b => (BookingStatus?)b.Status)
                    .SingleOrDefaultAsync(ct);
                return current is null
                    ? Results.NotFound() // not yours or doesn't exist: same answer
                    : Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: $"Booking cannot be cancelled because it is {current}.");
            }

            // Refund the payment in the SAME transaction as the cancellation.
            var payment = await db.Payments
                .SingleAsync(p => p.BookingId == id && p.Status == PaymentStatus.Succeeded, ct);
            var refund = await gateway.RefundAsync(
                new RefundRequest(payment.Id, payment.ProviderReference, payment.AmountHalalas, payment.Currency), ct);
            if (!refund.Succeeded)
            {
                await tx.RollbackAsync(ct); // booking stays Paid; customer can try again
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Refund failed. The booking was not cancelled.");
            }
            payment.MarkRefunded(refund.RefundReference, now);
            await db.SaveChangesAsync(ct);
            refunded = true;
        }

        await tx.CommitAsync(ct);

        var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id, ct);
        return Results.Ok(new CancelBookingResponse(BookingDto.From(booking), refunded));
    }

    // POST /bookings — the owner is the caller, the price is the business price. Neither comes from the body.
    private static async Task<IResult> Create(
        CreateBookingRequest request, ClaimsPrincipal principal, AppDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var name = request.PilgrimName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > Booking.PilgrimNameMaxLength)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pilgrimName"] = [$"Pilgrim name is required (1 to {Booking.PilgrimNameMaxLength} characters)."],
            });

        var booking = Booking.Create(
            customerId: principal.GetUserId(), // from the validated token
            pilgrimName: name,
            nowUtc: clock.GetUtcNow().UtcDateTime);

        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/bookings/{booking.Id}", BookingDto.From(booking));
    }

    // GET /bookings?limit=5 — only MY bookings, newest first, always a bounded page.
    private static async Task<IResult> ListMine(
        ClaimsPrincipal principal, AppDbContext db, CancellationToken ct, int? limit)
    {
        var customerId = principal.GetUserId();
        var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize); // never return an unbounded list

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.CustomerId == customerId) // ownership filter is in the SQL query itself
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Take(take)
            .ToListAsync(ct);

        return Results.Ok(bookings.Select(BookingDto.From));
    }

    // GET /bookings/{id} — someone else's booking returns 404, exactly like a booking that doesn't exist.
    // (403 would confirm "this id exists but isn't yours", which leaks information.)
    private static async Task<IResult> GetMine(
        Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var customerId = principal.GetUserId();

        var booking = await db.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id && b.CustomerId == customerId, ct);

        return booking is null ? Results.NotFound() : Results.Ok(BookingDto.From(booking));
    }
}
