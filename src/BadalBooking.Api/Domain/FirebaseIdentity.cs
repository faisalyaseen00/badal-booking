namespace BadalBooking.Api.Domain;

// Links one Firebase account (its uid) to one local user.
// Database rules: a Firebase uid maps to ONE user, and a user has at most ONE Firebase uid,
// so an existing link can never be silently replaced by another Firebase account.
public class FirebaseIdentity
{
    private FirebaseIdentity() { } // for EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string FirebaseUid { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }

    public static FirebaseIdentity Link(Guid userId, string firebaseUid, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        FirebaseUid = firebaseUid,
        CreatedAtUtc = nowUtc,
    };
}
