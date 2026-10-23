using System.Security.Claims;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BadalBooking.Api.Features.PerformerApplications;

public sealed record ApplyRequest(string? FullName, string? Phone, string? Note);
public sealed record DecisionRequest(string? Note);

public sealed record ApplicationDto(
    Guid Id, string FullName, string Phone, string? Note, string Status,
    DateTime CreatedAtUtc, DateTime? DecidedAtUtc, string? DecisionNote)
{
    public static ApplicationDto From(PerformerApplication a) => new(
        a.Id, a.FullName, a.Phone, a.Note, a.Status.ToString(), a.CreatedAtUtc, a.DecidedAtUtc, a.DecisionNote);
}

// What the admin sees: the application plus the applicant's email.
public sealed record AdminApplicationDto(ApplicationDto Application, Guid UserId, string Email, string CurrentRole);

public static class PerformerApplicationEndpoints
{
    public static IEndpointRouteBuilder MapPerformerApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        // Applicant side: customers apply and check their status.
        var mine = app.MapGroup("/performer-applications").WithTags("Performer applications").RequireAuthorization();
        mine.MapPost("/", Apply).RequireAuthorization(AuthClaims.CustomerPolicy);
        mine.MapGet("/me", Mine);

        // Admin side: admins only. Customers and performers get 403.
        var admin = app.MapGroup("/admin/performer-applications").WithTags("Admin").RequireAuthorization(AuthClaims.AdminPolicy);
        admin.MapGet("/", List);
        admin.MapPost("/{id:guid}/approve", Approve);
        admin.MapPost("/{id:guid}/reject", Reject);
        return app;
    }

    // POST /performer-applications — a customer asks to become a performer. One open application at a time.
    private static async Task<IResult> Apply(
        ApplyRequest request, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var fullName = request.FullName?.Trim();
        var phone = request.Phone?.Trim();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(fullName) || fullName.Length > PerformerApplication.FullNameMaxLength)
            errors["fullName"] = ["Full name is required."];
        if (string.IsNullOrEmpty(phone) || phone.Length > PerformerApplication.PhoneMaxLength || !phone.All(c => char.IsDigit(c) || "+ -()".Contains(c)))
            errors["phone"] = ["A valid phone number is required."];
        if (note?.Length > PerformerApplication.NoteMaxLength)
            errors["note"] = [$"Note must be at most {PerformerApplication.NoteMaxLength} characters."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var application = PerformerApplication.Submit(principal.GetUserId(), fullName!, phone!, note, clock.GetUtcNow().UtcDateTime);
        db.PerformerApplications.Add(application);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You already have an application waiting for review.");
        }
        return Results.Created("/performer-applications/me", ApplicationDto.From(application));
    }

    // GET /performer-applications/me — the caller's most recent application (204 No Content if they never applied).
    private static async Task<IResult> Mine(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var latest = await db.PerformerApplications.AsNoTracking()
            .Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return latest is null ? Results.NoContent() : Results.Ok(ApplicationDto.From(latest));
    }

    // GET /admin/performer-applications?status=Pending
    private static async Task<IResult> List(AppDbContext db, CancellationToken ct, string? status)
    {
        var wanted = Enum.TryParse<ApplicationStatus>(status, ignoreCase: true, out var s) ? s : ApplicationStatus.Pending;
        var rows = await db.PerformerApplications.AsNoTracking()
            .Where(a => a.Status == wanted)
            .OrderBy(a => a.CreatedAtUtc)
            .Take(100)
            .Join(db.Users, a => a.UserId, u => u.Id, (a, u) => new { a, u.Email, u.Role })
            .ToListAsync(ct);
        return Results.Ok(rows.Select(r => new AdminApplicationDto(ApplicationDto.From(r.a), r.a.UserId, r.Email, r.Role.ToString())));
    }

    // POST /admin/performer-applications/{id}/approve
    // ONE SQL statement: mark the application Approved AND change the user's role to Performer.
    // Both happen or neither does, and only if the application is still Pending and the user is
    // still a Customer, so approving twice (or racing a reject) can't do anything odd.
    private static async Task<IResult> Approve(
        Guid id, DecisionRequest? request, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var adminId = principal.GetUserId();
        var now = clock.GetUtcNow().UtcDateTime;
        var note = Trimmed(request?.Note);

        var promoted = await db.Database.SqlQuery<Guid>($"""
            WITH decided AS (
                UPDATE app.performer_applications
                   SET status = 'Approved', decided_by_user_id = {adminId}, decided_at_utc = {now}, decision_note = {note}
                 WHERE id = {id} AND status = 'Pending'
                RETURNING user_id
            )
            UPDATE app.users SET role = 'Performer'
             WHERE id IN (SELECT user_id FROM decided) AND role = 'Customer'
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        return promoted.Count == 1
            ? Results.Ok(new { approved = true })
            : await NotPendingAsync(db, id, ct);
    }

    // POST /admin/performer-applications/{id}/reject — the user stays a Customer and may apply again later.
    private static async Task<IResult> Reject(
        Guid id, DecisionRequest? request, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var adminId = principal.GetUserId();
        var now = clock.GetUtcNow().UtcDateTime;
        var note = Trimmed(request?.Note);

        var rejected = await db.PerformerApplications
            .Where(a => a.Id == id && a.Status == ApplicationStatus.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Status, ApplicationStatus.Rejected)
                .SetProperty(a => a.DecidedByUserId, adminId)
                .SetProperty(a => a.DecidedAtUtc, now)
                .SetProperty(a => a.DecisionNote, note), ct);

        return rejected == 1 ? Results.Ok(new { rejected = true }) : await NotPendingAsync(db, id, ct);
    }

    private static async Task<IResult> NotPendingAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        await db.PerformerApplications.AnyAsync(a => a.Id == id, ct)
            ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This application was already decided.")
            : Results.NotFound();

    private static string? Trimmed(string? note)
    {
        var t = note?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > PerformerApplication.DecisionNoteMaxLength ? t[..PerformerApplication.DecisionNoteMaxLength] : t;
    }
}
