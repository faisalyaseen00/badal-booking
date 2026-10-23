namespace BadalBooking.Api.Domain;

// An FCM registration token for one browser/phone, owned by the signed-in user who registered it.
// Token is unique: if another user signs in on the same device, the token moves to them,
// so the previous user's notifications stop going to that device.
public class DeviceToken
{
    public const int TokenMaxLength = 4096;
    public static readonly string[] Platforms = ["web", "android", "ios"];

    private DeviceToken() { } // for EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = "";
    public string Platform { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
}
