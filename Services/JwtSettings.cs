namespace BookIt.Api.Services;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    /// <summary>HMAC signing key, at least 32 characters. Override outside development.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Access token (JWT) lifetime. Short, because a JWT can't be revoked before it expires.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token lifetime. Revocable (stored in the DB), and each refresh issues a new one.</summary>
    public int RefreshTokenDays { get; set; } = 7;
}
