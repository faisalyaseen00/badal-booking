using System.Security.Claims;
using BadalBooking.Api.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BadalBooking.Api.Infrastructure.Auth;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

// Issues the API's own JWT. Used by /auth/login, /auth/register now,
// and by /auth/firebase in Phase 2, so every sign-in method ends with the same kind of token.
public sealed class JwtTokenService(JwtOptions options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(options.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            // Only what authorization needs. No email, no pilgrim data: JWTs are readable by anyone holding them.
            Subject = new ClaimsIdentity(
            [
                new Claim(AuthClaims.UserId, user.Id.ToString()),
                new Claim(AuthClaims.Role, user.Role.ToString()), // from the database, never from a request
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(options.KeyBytes), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedToken(_handler.CreateToken(descriptor), expires);
    }
}
