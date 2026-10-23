namespace BadalBooking.Api.Infrastructure.Payments;

public sealed record ChargeRequest(Guid BookingId, int AmountHalalas, string Currency, string IdempotencyKey);

public sealed record ChargeResult(bool Succeeded, string Provider, string ProviderReference);

public sealed record RefundRequest(Guid PaymentId, string ProviderReference, int AmountHalalas, string Currency);

public sealed record RefundResult(bool Succeeded, string RefundReference);

// The booking code only talks to this interface, so the simulator can later be swapped for a
// real HyperPay implementation without touching the endpoints.
//
// Note for a real gateway: HyperPay's flow is asynchronous (create checkout -> customer pays in
// HyperPay's widget -> server queries the payment status). That needs a "Pending" payment row,
// server-side status verification, and reconciliation of timeouts. See FIXES.md.
public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);

    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct);
}
