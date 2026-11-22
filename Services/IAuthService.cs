using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public interface IAuthService
{
    /// <summary>Creates a user. Returns null if the email is already registered.</summary>
    Task<UserResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>Validates credentials and returns a JWT. Returns null if the email or password is wrong.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
