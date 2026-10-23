using System.Collections.Concurrent;
using BadalBooking.Api.Infrastructure.Push;

namespace BadalBooking.Tests.Infrastructure;

// Stands in for FCM. Records what would have been sent; tests decide each token's outcome.
public sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(string Token, PushMessage Message)> Sent { get; } = new();
    public ConcurrentDictionary<string, PushOutcome> OutcomeByToken { get; } = new();

    public Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken ct)
    {
        var results = tokens.Select(t =>
        {
            var outcome = OutcomeByToken.GetValueOrDefault(t, PushOutcome.Sent);
            if (outcome == PushOutcome.Sent) Sent.Enqueue((t, message));
            return new PushResult(t, outcome, outcome == PushOutcome.Sent ? null : outcome.ToString());
        }).ToList();
        return Task.FromResult<IReadOnlyList<PushResult>>(results);
    }

    public IEnumerable<PushMessage> MessagesTo(string token) => Sent.Where(s => s.Token == token).Select(s => s.Message);
}
