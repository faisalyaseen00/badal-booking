using FirebaseAdmin;
using FirebaseAdmin.Messaging;

namespace BadalBooking.Api.Infrastructure.Push;

// Sends through Firebase Cloud Messaging and classifies each device's result.
public sealed class FcmPushSender(FirebaseApp app) : IPushSender
{
    public async Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken ct)
    {
        BatchResponse batch;
        try
        {
            batch = await FirebaseMessaging.GetMessaging(app).SendEachForMulticastAsync(new MulticastMessage
            {
                // FirebaseAdmin 3.7 marks Tokens obsolete in favour of Firebase Installation IDs (Fids),
                // but the web SDK's getToken() still returns FCM registration TOKENS, so Tokens is correct here.
#pragma warning disable CS0618
                Tokens = tokens.ToList(),
#pragma warning restore CS0618
                Notification = new Notification { Title = message.Title, Body = message.Body },
                Data = message.Data,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whole request failed (network, credentials, FCM down): nothing is known to be sent. Retry later.
            return tokens.Select(t => new PushResult(t, PushOutcome.TemporaryFailure, ex.GetType().Name)).ToList();
        }

        return tokens.Select((token, i) => Classify(token, batch.Responses[i])).ToList();
    }

    private static PushResult Classify(string token, SendResponse response)
    {
        if (response.IsSuccess) return new(token, PushOutcome.Sent);

        var code = response.Exception?.MessagingErrorCode;
        var outcome = code switch
        {
            // The app was uninstalled / the browser unsubscribed / the token expired.
            MessagingErrorCode.Unregistered => PushOutcome.InvalidToken,
            // Token belongs to a different Firebase project.
            MessagingErrorCode.SenderIdMismatch => PushOutcome.InvalidToken,
            // Malformed token. (Our message payload is fixed, so a bad argument here means a bad token.)
            MessagingErrorCode.InvalidArgument => PushOutcome.InvalidToken,
            // Unavailable, Internal, QuotaExceeded, ThirdPartyAuthError, unknown -> keep the token, retry.
            _ => PushOutcome.TemporaryFailure,
        };
        return new(token, outcome, code?.ToString() ?? "Unknown");
    }
}
