using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Dtos;

/// <summary>Body for /api/auth/refresh.</summary>
public class RefreshTokenRequest
{
    // Issued tokens are 86 characters (64 random bytes, base64url); the cap just rejects junk early
    [Required, MaxLength(200)]
    public string RefreshToken { get; set; } = string.Empty;
}
