using System.Net.Mail;
using System.Security.Claims;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
using BadalBooking.Api.Infrastructure.Firebase;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BadalBooking.Api.Features.Auth;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    private const int EmailMaxLength = 256;
    private const int PasswordMinLength = 8;
    private const int PasswordMaxLength = 128;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");

        // Brute-force protection only on the anonymous endpoints.
        auth.MapPost("/register", Register).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/login", Login).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/firebase", FirebaseSignIn).RequireRateLimiting(RateLimitPolicy);
        auth.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    // POST /auth/firebase { "idToken": "<Firebase ID token from the client SDK>" }
    // Verify the token with Firebase, find or create the local user, return OUR JWT
    // (the same kind of token /auth/login returns). The Firebase token is never stored or logged.
    private static async Task<IResult> FirebaseSignIn(
        FirebaseSignInRequest request, IServiceProvider services, FirebaseSignInService signIn,
        JwtTokenService tokens, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken) || request.IdToken.Length > 8192)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["idToken"] = ["A Firebase ID token is required."] });

        var verifier = services.GetService<IFirebaseTokenVerifier>();
        if (verifier is null)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Firebase sign-in is not configured.");

        VerifiedFirebaseUser firebaseUser;
        try
        {
            firebaseUser = await verifier.VerifyAsync(request.IdToken, ct);
        }
        catch (FirebaseVerificationException ex) when (ex.Failure == FirebaseVerifyFailure.ServiceUnavailable)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Firebase is temporarily unavailable.");
        }
        catch (FirebaseVerificationException)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid Firebase ID token.");
        }

        var result = await signIn.SignInAsync(firebaseUser, ct);
        return result.Error switch
        {
            null => Results.Ok(ToResponse(result.User!, tokens.Issue(result.User!))),
            FirebaseSignInError.EmailMissing => Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "The Firebase account has no email address."),
            FirebaseSignInError.EmailNotVerified => Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Verify your email address in Firebase before signing in."),
            // Same message for both, so the response doesn't reveal that an email belongs to a performer.
            _ => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This email can't be used with Firebase sign-in. Sign in with your password instead."),
        };
    }

    // POST /auth/register — always creates a CUSTOMER. There is no public way to become a Performer.
    private static async Task<IResult> Register(
        RegisterRequest request, AppDbContext db, IPasswordHasher<User> hasher,
        JwtTokenService tokens, TimeProvider clock, CancellationToken ct)
    {
        var errors = Validate(request.Email, request.Password);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var user = User.CreateCustomer(request.Email!, clock.GetUtcNow().UtcDateTime);
        user.SetPasswordHash(hasher.HashPassword(user, request.Password!));
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        // No "check if email exists, then insert": two requests at once could both pass the check.
        // The unique index decides, and we turn its error into a clean 409.
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email is already registered.");
        }

        return Results.Ok(ToResponse(user, tokens.Issue(user)));
    }

    // POST /auth/login — same 401 for "no such email" and "wrong password",
    // so attackers can't use login to discover which emails have accounts.
    private static async Task<IResult> Login(
        LoginRequest request, AppDbContext db, IPasswordHasher<User> hasher,
        JwtTokenService tokens, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return InvalidCredentials();

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, ct);

        if (user?.PasswordHash is null)
        {
            // Still do a full hash check so "unknown email" takes as long as "wrong password"
            // (otherwise response time would reveal which emails exist).
            hasher.VerifyHashedPassword(null!, DummyHash.Value, request.Password);
            return InvalidCredentials(); // also covers Firebase-only users who have no password
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return InvalidCredentials();

        return Results.Ok(ToResponse(user, tokens.Issue(user)));
    }

    // GET /auth/me — who am I? Reads the id from the token, then the CURRENT role from the database.
    private static async Task<IResult> Me(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(ToDto(user));
    }

    private static Dictionary<string, string[]> Validate(string? email, string? password)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > EmailMaxLength
            || !MailAddress.TryCreate(email.Trim(), out var parsed) || parsed.Address != email.Trim())
            errors["email"] = ["A valid email address is required."];

        if (string.IsNullOrEmpty(password) || password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
            errors["password"] = [$"Password must be {PasswordMinLength} to {PasswordMaxLength} characters."];

        return errors;
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.Role.ToString());

    private static AuthResponse ToResponse(User user, IssuedToken token) =>
        new(token.AccessToken, token.ExpiresAtUtc, ToDto(user));

    // A real hash of a random password, computed once, used only for timing equalisation.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<User>().HashPassword(null!, Guid.NewGuid().ToString()));
}
