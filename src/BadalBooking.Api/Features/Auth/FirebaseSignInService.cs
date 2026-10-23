using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Firebase;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BadalBooking.Api.Features.Auth;

public enum FirebaseSignInError
{
    EmailMissing,       // Firebase account has no email
    EmailNotVerified,   // verify the email in Firebase first
    PerformerAccount,   // email belongs to a performer: never linked
    AlreadyLinked,      // that local account is linked to a different Firebase account
}

public sealed record FirebaseSignInResult(User? User, FirebaseSignInError? Error);

// Decides which local account a VERIFIED Firebase user becomes. Rules, in order:
//  1. Firebase uid already linked        -> that user (its role comes from OUR database).
//  2. No email / email not verified       -> refuse. (Prevents someone claiming an email they don't own.)
//  3. Email belongs to a Performer/Admin  -> refuse. A Firebase login can never take over those by email.
//  4. Email belongs to a Customer         -> link, unless that customer is already linked to another uid.
//  5. Email unknown                       -> create a new Customer and link.
public sealed class FirebaseSignInService(AppDbContext db, TimeProvider clock)
{
    public async Task<FirebaseSignInResult> SignInAsync(VerifiedFirebaseUser firebaseUser, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ResolveAsync(firebaseUser, ct);
            }
            catch (DbUpdateException ex) when (attempt == 1 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two sign-ins for the same new user at the same moment: the other one created it first.
                // Start over once; rule 1 or 4 will now find it.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<FirebaseSignInResult> ResolveAsync(VerifiedFirebaseUser fb, CancellationToken ct)
    {
        // 1. Known Firebase account.
        var linked = await db.FirebaseIdentities.AsNoTracking()
            .Where(i => i.FirebaseUid == fb.Uid)
            .Join(db.Users, i => i.UserId, u => u.Id, (_, u) => u)
            .SingleOrDefaultAsync(ct);
        if (linked is not null) return new(linked, null);

        // 2. A new link needs a verified email.
        if (string.IsNullOrWhiteSpace(fb.Email)) return new(null, FirebaseSignInError.EmailMissing);
        if (!fb.EmailVerified) return new(null, FirebaseSignInError.EmailNotVerified);

        var email = User.NormalizeEmail(fb.Email);
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);

        // 3. Never take over a performer or admin by email. (Approved performers who signed up with
        //    Firebase are found by rule 1 via their uid, so they still sign in normally.)
        if (existing is not null && existing.Role != UserRole.Customer) return new(null, FirebaseSignInError.PerformerAccount);

        if (existing is not null)
        {
            // 4. Link an existing customer, but never replace an existing link.
            var linkedUid = await db.FirebaseIdentities
                .Where(i => i.UserId == existing.Id).Select(i => i.FirebaseUid).SingleOrDefaultAsync(ct);
            // Same uid: a parallel request for THIS Firebase user created the account a moment ago
            // (after our rule-1 check). It's the same person, so sign them in. (Race found by the
            // "simultaneous first sign-ins" test: before this, one request in five could get 409.)
            if (linkedUid == fb.Uid) return new(existing, null);
            if (linkedUid is not null) return new(null, FirebaseSignInError.AlreadyLinked);

            db.FirebaseIdentities.Add(FirebaseIdentity.Link(existing.Id, fb.Uid, now));
            await db.SaveChangesAsync(ct);
            return new(existing, null);
        }

        // 5. New customer. User + link are saved together in one SaveChanges (one transaction).
        var user = User.CreateCustomer(email, now); // no password: this user signs in with Firebase
        db.Users.Add(user);
        db.FirebaseIdentities.Add(FirebaseIdentity.Link(user.Id, fb.Uid, now));
        await db.SaveChangesAsync(ct);
        return new(user, null);
    }
}
