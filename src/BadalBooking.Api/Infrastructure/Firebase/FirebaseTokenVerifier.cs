using FirebaseAdmin;
using FirebaseAdmin.Auth;

namespace BadalBooking.Api.Infrastructure.Firebase;

// What we trust from a Firebase ID token AFTER the Admin SDK has verified it.
public sealed record VerifiedFirebaseUser(string Uid, string? Email, bool EmailVerified);

public enum FirebaseVerifyFailure { InvalidToken, ServiceUnavailable }

public sealed class FirebaseVerificationException(FirebaseVerifyFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public FirebaseVerifyFailure Failure { get; } = failure;
}

// Interface so tests can use a fake (step 2.7) without real Firebase tokens.
public interface IFirebaseTokenVerifier
{
    Task<VerifiedFirebaseUser> VerifyAsync(string idToken, CancellationToken ct);
}

// Real verification with the Firebase Admin SDK. VerifyIdTokenAsync checks:
//  - signature against Google's public keys (so the token can't be forged or edited)
//  - audience + issuer = OUR Firebase project (a token from another project is rejected)
//  - expiry and issued-at time
//  - checkRevoked: true -> also rejects tokens of disabled users or after "revoke sessions"
// We never just decode the JWT ourselves: decoding is not verifying.
public sealed class FirebaseTokenVerifier(FirebaseApp app) : IFirebaseTokenVerifier
{
    public async Task<VerifiedFirebaseUser> VerifyAsync(string idToken, CancellationToken ct)
    {
        FirebaseToken token;
        try
        {
            token = await FirebaseAuth.GetAuth(app).VerifyIdTokenAsync(idToken, checkRevoked: true, ct);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode is AuthErrorCode.CertificateFetchFailed)
        {
            // Google's keys couldn't be downloaded: our problem, not the user's. Don't call it "invalid".
            throw new FirebaseVerificationException(FirebaseVerifyFailure.ServiceUnavailable, "Firebase is unreachable.", ex);
        }
        catch (FirebaseAuthException ex)
        {
            // Invalid signature, wrong project, expired, revoked, disabled user, malformed...
            throw new FirebaseVerificationException(FirebaseVerifyFailure.InvalidToken, "Invalid Firebase ID token.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new FirebaseVerificationException(FirebaseVerifyFailure.InvalidToken, "Malformed Firebase ID token.", ex);
        }

        token.Claims.TryGetValue("email", out var email);
        token.Claims.TryGetValue("email_verified", out var verified);
        return new VerifiedFirebaseUser(token.Uid, email as string, verified is true);
    }
}
