using BookIt.Api.Data;
using BookIt.Api.Dtos;
using BookIt.Api.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookIt.Api.Services;

public class AuthService : IAuthService
{
    private readonly BookItDbContext _db;

    private readonly JwtSettings _jwt;

    // Valid bcrypt hash used to keep timing similar when the email doesn't exist
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password");

    public AuthService(BookItDbContext db, IOptions<JwtSettings> jwt)
    {
        _db = db;
        _jwt = jwt.Value;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !passwordOk)
        {
            return null;
        }

        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expires, signingCredentials: creds);

        return new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            new UserResponse(user.Id, user.Email, user.FullName, user.Role));
    }

    public async Task<UserResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return null;
        }

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index violation from a concurrent registration with the same email
            return null;
        }

        return new UserResponse(user.Id, user.Email, user.FullName, user.Role);
    }
}