using BadalBooking.Api.Infrastructure.Firebase;

namespace BadalBooking.Tests.Infrastructure;

// Stands in for the Firebase Admin SDK so the ACCOUNT RULES can be tested without real Firebase.
// (Real token verification was demonstrated against the live project in step 2.2.)
//
// A test "token" is just a description of the already-verified user:
//   FakeFirebaseVerifier.Token(uid, email, emailVerified)
//   "invalid"      -> rejected like a forged/expired token
//   "firebase-down" -> Firebase unreachable
public sealed class FakeFirebaseVerifier : IFirebaseTokenVerifier
{
    public static string Token(string uid, string? email, bool verified) => $"fake|{uid}|{email}|{verified}";

    public Task<VerifiedFirebaseUser> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (idToken == "firebase-down")
            throw new FirebaseVerificationException(FirebaseVerifyFailure.ServiceUnavailable, "down");

        var parts = idToken.Split('|');
        if (parts.Length != 4 || parts[0] != "fake")
            throw new FirebaseVerificationException(FirebaseVerifyFailure.InvalidToken, "invalid");

        return Task.FromResult(new VerifiedFirebaseUser(parts[1], parts[2].Length == 0 ? null : parts[2], bool.Parse(parts[3])));
    }
}
