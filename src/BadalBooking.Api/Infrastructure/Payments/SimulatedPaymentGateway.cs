namespace BadalBooking.Api.Infrastructure.Payments;

// A fake gateway for the demo: no money moves, every charge and refund "succeeds".
// Clearly labelled everywhere ("Simulated" provider, "SIM-"/"SIMREF-" references, startup warning)
// so it can never be mistaken for a real HyperPay transaction.
public sealed class SimulatedPaymentGateway : IPaymentGateway
{
    public const string ProviderName = "Simulated";

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct) =>
        Task.FromResult(new ChargeResult(
            Succeeded: true,
            Provider: ProviderName,
            ProviderReference: $"SIM-{Guid.NewGuid():N}"));

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult(
            Succeeded: true,
            RefundReference: $"SIMREF-{Guid.NewGuid():N}"));
}
