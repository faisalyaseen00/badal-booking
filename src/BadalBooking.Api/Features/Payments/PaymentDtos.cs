using BadalBooking.Api.Domain;

namespace BadalBooking.Api.Features.Payments;

// No request body: the amount is never sent by the client. The only input is the
// Idempotency-Key header and the booking id in the URL.
public sealed record PaymentDto(
    Guid Id,
    Guid BookingId,
    int AmountHalalas,
    decimal Amount,
    string Currency,
    string Status,          // Succeeded or Refunded
    string Provider,
    string ProviderReference,
    bool Simulated,         // true = no real money moved
    DateTime CreatedAtUtc,
    DateTime? RefundedAtUtc)
{
    public static PaymentDto From(Payment p) => new(
        p.Id, p.BookingId, p.AmountHalalas, p.AmountHalalas / 100m, p.Currency, p.Status.ToString(),
        p.Provider, p.ProviderReference, p.Provider == Infrastructure.Payments.SimulatedPaymentGateway.ProviderName,
        p.CreatedAtUtc, p.RefundedAtUtc);
}
