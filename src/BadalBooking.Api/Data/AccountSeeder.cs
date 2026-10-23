using BadalBooking.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BadalBooking.Api.Data;

// Accounts that can't be created through the public API are created at startup from server settings:
//   Seed__AdminEmail / Seed__AdminPassword          -> the one Admin (approves performer applications)
//   Seed__PerformerEmail / Seed__PerformerPassword  -> an optional demo performer
//
// Safe to run on every startup and on several instances at once:
//  - account already exists -> nothing changes (its password and role are never overwritten)
//  - an existing account with a different role is NOT promoted: config must never silently
//    turn a real customer into a performer or an admin
public static class AccountSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await SeedOneAsync(services, "Admin", UserRole.Admin, User.CreateAdmin, ct);
        await SeedOneAsync(services, "Performer", UserRole.Performer, User.CreatePerformer, ct);
    }

    private static async Task SeedOneAsync(IServiceProvider services, string name, UserRole role,
        Func<string, DateTime, User> create, CancellationToken ct)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AccountSeeder));

        var email = config[$"Seed:{name}Email"];
        var password = config[$"Seed:{name}Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            logger.LogInformation("No seed {Role} configured (Seed__{Role}Email is empty). Skipping.", name, name);
            return;
        }
        if (password.Length < 12)
            throw new InvalidOperationException($"Seed__{name}Password must be at least 12 characters.");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = User.NormalizeEmail(email);

        var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == normalized, ct);
        if (existing is not null)
        {
            if (existing.Role != role)
                logger.LogWarning("Seed {Role} email belongs to an existing {Existing} account. It was NOT changed.", name, existing.Role);
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var user = create(normalized, clock.GetUtcNow().UtcDateTime);
        user.SetPasswordHash(hasher.HashPassword(user, password));
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed {Role} account created.", name);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another instance created it at the same moment. That's fine.
        }
    }
}
