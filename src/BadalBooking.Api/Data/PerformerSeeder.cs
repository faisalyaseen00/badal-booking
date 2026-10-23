using BadalBooking.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BadalBooking.Api.Data;

// Performers can't sign up through the API. The one demo performer is created at startup
// from server-side settings (Seed__PerformerEmail / Seed__PerformerPassword).
//
// Safe to run on every startup and on several instances at once:
//  - account already exists -> nothing changes (its password and role are never overwritten)
//  - an existing CUSTOMER with that email is NOT promoted: config must not silently turn a
//    real customer into a performer
public static class PerformerSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(PerformerSeeder));

        var email = config["Seed:PerformerEmail"];
        var password = config["Seed:PerformerPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            logger.LogInformation("No demo performer configured (Seed__PerformerEmail is empty). Skipping.");
            return;
        }
        if (password.Length < 12)
            throw new InvalidOperationException("Seed__PerformerPassword must be at least 12 characters.");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = User.NormalizeEmail(email);

        var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == normalized, ct);
        if (existing is not null)
        {
            if (existing.Role != UserRole.Performer)
                logger.LogWarning("Seed performer email belongs to an existing {Role} account. It was NOT promoted.", existing.Role);
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var performer = User.CreatePerformer(normalized, clock.GetUtcNow().UtcDateTime);
        performer.SetPasswordHash(hasher.HashPassword(performer, password));
        db.Users.Add(performer);

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Demo performer account created.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another instance created it at the same moment. That's fine.
        }
    }
}
