using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public interface IAuthService
{
    /// <summary>Creates a user. Returns null if the email is already registered.</summary>
    Task<UserResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>Validates credentials and returns an access token + refresh token. Returns null if the email or password is wrong.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Exchanges a valid refresh token for a new access token + refresh token (the old refresh token is revoked).
    /// Returns null if the token is unknown, expired or revoked. Presenting an already-used token revokes its whole family.
    /// </summary>
    Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
