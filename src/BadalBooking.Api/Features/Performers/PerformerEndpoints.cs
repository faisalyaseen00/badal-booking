using System.Security.Claims;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Features.Performers;

// What a performer sees: just enough to perform the Umrah. No customer id, email or payment data.
public sealed record AvailableBookingDto(Guid Id, string PilgrimName, DateTime CreatedAtUtc);

public sealed record AcceptBookingResponse(Guid BookingId, string Status, DateTime AcceptedAtUtc);

public static class PerformerEndpoints
{
    private const int MaxAvailable = 50;

    public static IEndpointRouteBuilder MapPerformerEndpoints(this IEndpointRouteBuilder app)
    {
        // Performer-only. A Customer token gets 403.
        var performer = app.MapGroup("/bookings")
            .WithTags("Performers")
            .RequireAuthorization(AuthClaims.PerformerPolicy);

        performer.MapGet("/available", ListAvailable);
        performer.MapPost("/{id:guid}/accept", Accept);

        return app;
    }

    // GET /bookings/available — paid bookings nobody has accepted yet, oldest first (fair queue).
    private static async Task<IResult> ListAvailable(AppDbContext db, CancellationToken ct)
    {
        var available = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Paid && b.PerformerId == null)
            .OrderBy(b => b.CreatedAtUtc)
            .Take(MaxAvailable)
            .Select(b => new AvailableBookingDto(b.Id, b.PilgrimName, b.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(available);
    }

    // POST /bookings/{id}/accept — only ONE performer can ever win a booking.
    //
    // This is a single conditional UPDATE, not "read, check, then save":
    //   UPDATE bookings SET status='Accepted', performer_id=@me
    //   WHERE id=@id AND status='Paid' AND performer_id IS NULL
    // If 10 performers press Accept at the same moment, PostgreSQL row locking makes them run one
    // after another; the first changes 1 row, the other 9 find the WHERE no longer true (0 rows) -> 409.
    // The same WHERE also loses cleanly against a customer cancelling at the same time.
    private static async Task<IResult> Accept(
        Guid id, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var performerId = principal.GetUserId();
        var now = clock.GetUtcNow().UtcDateTime;

        var accepted = await db.Bookings
            .Where(b => b.Id == id && b.Status == BookingStatus.Paid && b.PerformerId == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(b => b.Status, BookingStatus.Accepted)
                .SetProperty(b => b.PerformerId, performerId)
                .SetProperty(b => b.UpdatedAtUtc, now), ct);

        // Step 2.5 will add the "booking accepted" push notification here, in the same transaction.

        if (accepted == 1)
            return Results.Ok(new AcceptBookingResponse(id, nameof(BookingStatus.Accepted), now));

        var exists = await db.Bookings.AnyAsync(b => b.Id == id, ct);
        return exists
            ? Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Booking is not available: it was already accepted, or it is not paid, or it was cancelled.")
            : Results.NotFound();
    }
}
