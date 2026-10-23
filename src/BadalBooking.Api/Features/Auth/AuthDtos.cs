namespace BadalBooking.Api.Features.Auth;

// Request DTOs contain ONLY what the client is allowed to decide.
// There is deliberately no "Role" field: sending "role": "Performer" is simply ignored.
public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

// Responses never include the password hash.
public sealed record UserDto(Guid Id, string Email, string Role);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);
