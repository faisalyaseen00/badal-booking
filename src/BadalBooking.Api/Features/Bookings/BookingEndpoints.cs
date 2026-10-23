using System.Security.Claims;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
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

        return app;
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
