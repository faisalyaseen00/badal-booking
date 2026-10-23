using BadalBooking.Api.Domain;

namespace BadalBooking.Api.Features.Bookings;

// What the customer is allowed to decide: ONLY the pilgrim's name.
// No price, currency, status, customer id or performer id. If a client sends
// "amountHalalas": 1 or "customerId": "<someone else>", those fields are ignored
// because they don't exist here, and the server sets them itself.
public sealed record CreateBookingRequest(string? PilgrimName);

// Refunded = true when the booking had been paid and the (simulated) payment was refunded.
public sealed record CancelBookingResponse(BookingDto Booking, bool Refunded);

// What the API returns. Built from the database row, never echoed from the request.
public sealed record BookingDto(
    Guid Id,
    string PilgrimName,
    int AmountHalalas,      // 50000
    decimal Amount,         // 500.00, for display
    string Currency,        // "SAR"
    string Status,
    DateTime CreatedAtUtc,
    DateTime PaymentDeadlineUtc,
    DateTime UpdatedAtUtc)
{
    // The performer's id is deliberately not exposed to customers.
    public static BookingDto From(Booking b) => new(
        b.Id, b.PilgrimName, b.AmountHalalas, b.AmountHalalas / 100m, b.Currency,
        b.Status.ToString(), b.CreatedAtUtc, b.PaymentDeadlineUtc, b.UpdatedAtUtc);
}
