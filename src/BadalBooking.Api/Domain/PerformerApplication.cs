namespace BadalBooking.Api.Domain;

public enum ApplicationStatus { Pending, Approved, Rejected }

// A customer asking to become a performer. Only an Admin can approve it; approval changes the
// user's role to Performer in the same SQL statement. Database rule: at most ONE Pending
// application per user.
public class PerformerApplication
{
    public const int FullNameMaxLength = 200;
    public const int PhoneMaxLength = 30;
    public const int NoteMaxLength = 1000;
    public const int DecisionNoteMaxLength = 500;

    private PerformerApplication() { } // for EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string FullName { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string? Note { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }

    public static PerformerApplication Submit(Guid userId, string fullName, string phone, string? note, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        FullName = fullName,
        Phone = phone,
        Note = note,
        Status = ApplicationStatus.Pending,
        CreatedAtUtc = nowUtc,
    };
}
