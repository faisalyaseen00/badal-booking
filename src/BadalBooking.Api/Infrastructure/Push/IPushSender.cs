namespace BadalBooking.Api.Infrastructure.Push;

public sealed record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string> Data);

public enum PushOutcome
{
    Sent,
    InvalidToken,      // FCM says this token will never work again -> delete it
    TemporaryFailure,  // network / quota / FCM outage -> try again later
}

public sealed record PushResult(string Token, PushOutcome Outcome, string? Error = null);

// Interface so tests can use a fake instead of real FCM.
public interface IPushSender
{
    Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken ct);
}
