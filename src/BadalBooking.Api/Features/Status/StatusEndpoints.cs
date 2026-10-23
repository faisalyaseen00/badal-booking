using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Features.Status;

// Deliberately minimal: a short reference, status, price and time. No pilgrim names, no customer
// data, no full ids. This endpoint is public (the Vercel status page calls it without signing in).
public sealed record DemoBookingDto(string Reference, string Status, decimal Amount, string Currency, DateTime CreatedAtUtc);

public sealed record DemoBookingsResponse(bool Configured, string? DemoCustomer, IReadOnlyList<DemoBookingDto> Bookings);

public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /status/demo-bookings — the last 5 bookings of ONE demo customer chosen on the server
        // (Demo__CustomerEmail). Nobody can ask for another customer's bookings through this.
        app.MapGet("/status/demo-bookings", async (AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var email = config["Demo:CustomerEmail"];
            if (string.IsNullOrWhiteSpace(email))
                return Results.Ok(new DemoBookingsResponse(false, null, []));

            var normalized = User.NormalizeEmail(email);
            var bookings = await db.Bookings.AsNoTracking()
                .Where(b => db.Users.Any(u => u.Id == b.CustomerId && u.Email == normalized))
                .OrderByDescending(b => b.CreatedAtUtc)
                .Take(5)
                .Select(b => new { b.Id, b.Status, b.AmountHalalas, b.Currency, b.CreatedAtUtc })
                .ToListAsync(ct);

            return Results.Ok(new DemoBookingsResponse(true, MaskEmail(normalized), bookings
                .Select(b => new DemoBookingDto(b.Id.ToString()[..8], b.Status.ToString(), b.AmountHalalas / 100m, b.Currency, b.CreatedAtUtc))
                .ToList()));
        }).WithTags("Status");

        return app;
    }

    // "demo.customer@example.com" -> "de***@example.com"
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 2 ? "***" + email[Math.Max(at, 0)..] : email[..2] + "***" + email[at..];
    }
}
