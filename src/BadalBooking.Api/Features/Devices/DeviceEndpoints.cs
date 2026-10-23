using System.Security.Claims;
using BadalBooking.Api.Data;
using BadalBooking.Api.Domain;
using BadalBooking.Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Api.Features.Devices;

public sealed record RegisterDeviceRequest(string? Token, string? Platform);
public sealed record DeviceDto(Guid Id, string Platform);
public sealed record LogoutRequest(string? DeviceToken);

internal sealed record UpsertedDevice(Guid Id, bool Created);

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        // Any signed-in user (customer or performer). No token -> 401.
        app.MapPost("/devices", Register).WithTags("Devices").RequireAuthorization();
        app.MapDelete("/devices/{id:guid}", Delete).WithTags("Devices").RequireAuthorization();
        app.MapPost("/auth/logout", Logout).WithTags("Auth").RequireAuthorization();
        return app;
    }

    // POST /devices { "token": "<FCM registration token>", "platform": "web" }
    // The owner is the caller (from the JWT), never from the body.
    // One SQL statement inserts the token, or, if it already exists (same browser, maybe another user),
    // moves it to the caller. Atomic, so two registrations at once can't create duplicates.
    private static async Task<IResult> Register(
        RegisterDeviceRequest request, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var token = request.Token?.Trim();
        var platform = (request.Platform ?? "web").Trim().ToLowerInvariant();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(token) || token.Length > DeviceToken.TokenMaxLength)
            errors["token"] = ["A device token is required."];
        if (!DeviceToken.Platforms.Contains(platform))
            errors["platform"] = [$"Platform must be one of: {string.Join(", ", DeviceToken.Platforms)}."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var userId = principal.GetUserId();
        var now = clock.GetUtcNow().UtcDateTime;

        var saved = (await db.Database.SqlQuery<UpsertedDevice>($"""
            INSERT INTO app.device_tokens (id, user_id, token, platform, created_at_utc, updated_at_utc)
            VALUES ({Guid.CreateVersion7()}, {userId}, {token}, {platform}, {now}, {now})
            ON CONFLICT (token) DO UPDATE
               SET user_id = EXCLUDED.user_id, platform = EXCLUDED.platform, updated_at_utc = EXCLUDED.updated_at_utc
            RETURNING id, (xmax = 0) AS created
            """).ToListAsync(ct)).Single();

        // The token itself is never echoed back or logged.
        var body = new DeviceDto(saved.Id, platform);
        return saved.Created ? Results.Created($"/devices/{saved.Id}", body) : Results.Ok(body);
    }

    // DELETE /devices/{id} — only your own device; someone else's -> 404.
    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var deleted = await db.DeviceTokens.Where(d => d.Id == id && d.UserId == userId).ExecuteDeleteAsync(ct);
        return deleted == 1 ? Results.NoContent() : Results.NotFound();
    }

    // POST /auth/logout { "deviceToken": "<FCM token of this device>" }
    // Removes this device's token so it gets no more pushes for this user. Safe to call twice.
    // Only deletes the token if it belongs to the caller.
    // Note: our JWTs are stateless, so the client must also throw its JWT away (it expires within 60 min).
    private static async Task<IResult> Logout(LogoutRequest request, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var token = request.DeviceToken?.Trim();
        if (!string.IsNullOrEmpty(token))
        {
            var userId = principal.GetUserId();
            await db.DeviceTokens.Where(d => d.Token == token && d.UserId == userId).ExecuteDeleteAsync(ct);
        }
        return Results.NoContent();
    }
}
