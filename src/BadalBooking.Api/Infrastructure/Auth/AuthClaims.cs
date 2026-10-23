using System.Security.Claims;
using BadalBooking.Api.Domain;

namespace BadalBooking.Api.Infrastructure.Auth;

// One place for claim names and the policies built on them.
public static class AuthClaims
{
    public const string UserId = "sub";
    public const string Role = "role";

    public const string CustomerPolicy = "CustomerOnly";
    public const string PerformerPolicy = "PerformerOnly";

    // The user id ALWAYS comes from the validated token, never from the URL or body.
    // That is what stops "user A acts as user B" (IDOR) in the booking endpoints.
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserId), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated user has no valid 'sub' claim.");

    public static bool IsRole(this ClaimsPrincipal principal, UserRole role) =>
        principal.HasClaim(Role, role.ToString());
}
