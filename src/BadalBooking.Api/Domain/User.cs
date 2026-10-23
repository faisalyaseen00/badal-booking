namespace BadalBooking.Api.Domain;

public class User
{
    // EF Core uses this to load rows. Application code must use the factory methods below.
    private User() { }

    public Guid Id { get; private set; }

    // Always stored lower-case and trimmed, so "A@x.com" and "a@x.com" are the same account.
    public string Email { get; private set; } = "";

    // Null for users who only sign in with Firebase (Phase 2).
    public string? PasswordHash { get; private set; }

    // Set only on the server. No request DTO will ever contain a role.
    public UserRole Role { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    // Single rule for comparing emails everywhere (register, login, Firebase linking).
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // The hash is computed by the auth layer (ASP.NET Core PasswordHasher). The plain password is never stored.
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public static User CreateCustomer(string email, DateTime nowUtc) => Create(email, UserRole.Customer, nowUtc);

    // Only used by the server-side seed (step 1.6). There is no public way to become a Performer.
    public static User CreatePerformer(string email, DateTime nowUtc) => Create(email, UserRole.Performer, nowUtc);

    // Only used by the server-side seed. There is no public way to become an Admin.
    public static User CreateAdmin(string email, DateTime nowUtc) => Create(email, UserRole.Admin, nowUtc);

    private static User Create(string email, UserRole role, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = NormalizeEmail(email),
        Role = role,
        CreatedAtUtc = nowUtc,
    };
}
