using System.Text;

namespace BadalBooking.Api.Infrastructure.Auth;

// Bound from the "Jwt" config section: Jwt__Key, Jwt__Issuer, Jwt__Audience (see .env).
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public int LifetimeMinutes { get; init; } = 60;

    public byte[] KeyBytes => Encoding.UTF8.GetBytes(Key);

    // Fail at startup with a clear message instead of failing later on the first login.
    public static JwtOptions LoadAndValidate(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (options.KeyBytes.Length < 32)
            throw new InvalidOperationException("Jwt__Key must be set and at least 32 characters (256 bits) for HS256.");
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException("Jwt__Issuer and Jwt__Audience must be set.");
        if (options.LifetimeMinutes is < 1 or > 24 * 60)
            throw new InvalidOperationException("Jwt__LifetimeMinutes must be between 1 and 1440.");

        return options;
    }
}
