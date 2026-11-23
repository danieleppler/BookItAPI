using BookIt.Api.Data;
using BookIt.Api.Dtos;
using BookIt.Api.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace BookIt.Api.Services;

public class AuthService : IAuthService
{
    private readonly BookItDbContext _db;

    private readonly JwtSettings _jwt;

    private readonly ILogger<AuthService> _logger;

    // Valid bcrypt hash used to keep timing similar when the email doesn't exist
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password");

    public AuthService(BookItDbContext db, IOptions<JwtSettings> jwt, ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt.Value;
        _logger = logger;
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

        // A new login starts a new token family (one per device/session)
        var response = IssueTokens(user, familyId: Guid.NewGuid());
        await _db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var hash = HashToken(refreshToken);

        // The user is loaded fresh, so a role change takes effect on the next refresh instead of lasting until re-login
        var stored = await _db.RefreshTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => new { t.Id, t.FamilyId, t.ExpiresAtUtc, t.RevokedAtUtc, t.User })
            .SingleOrDefaultAsync(ct);

        if (stored is null)
        {
            return null;
        }

        if (stored.RevokedAtUtc is not null)
        {
            // Each refresh token works once. Seeing a used one again means two parties hold it (the real client
            // and an attacker), and we can't tell which is which, so the whole session is killed; the real user logs in again.
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            _logger.LogWarning("Refresh token reuse detected for user {UserId}; revoked token family {FamilyId}",
                stored.User.Id, stored.FamilyId);
            return null;
        }

        if (stored.ExpiresAtUtc <= now)
        {
            return null;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Conditional update = atomic "use once". If two requests race with the same token, Postgres row-locks it:
        // the second waits, re-checks "RevokedAtUtc IS NULL" after the first commits, and updates 0 rows.
        var revoked = await _db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);

        if (revoked == 0)
        {
            await transaction.RollbackAsync(ct);
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            _logger.LogWarning("Concurrent reuse of a refresh token for user {UserId}; revoked token family {FamilyId}",
                stored.User.Id, stored.FamilyId);
            return null;
        }

        // Rotation: the new refresh token stays in the same family, so later reuse of the old one can revoke this one too
        var response = IssueTokens(stored.User, stored.FamilyId);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return response;
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
            Role = UserRole.Client, // never client-controlled; elevated only via admin flow
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Users_Email",
        })
        {
            // Concurrent registration with the same email won the race.
            // Any other DB failure (connection, timeout, other constraint) propagates as a 500.
            return null;
        }

        return new UserResponse(user.Id, user.Email, user.FullName, user.Role);
    }

    /// <summary>Creates a signed JWT and a new refresh token row (added to the context; the caller saves).</summary>
    private LoginResponse IssueTokens(User user, Guid familyId)
    {
        var now = DateTime.UtcNow;

        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: accessExpires, signingCredentials: creds);

        // 64 bytes from a cryptographic RNG: unguessable, and URL-safe so it's easy to send and store client-side
        var refreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            FamilyId = familyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpires,
        });

        return new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            accessExpires,
            refreshToken,
            refreshExpires,
            new UserResponse(user.Id, user.Email, user.FullName, user.Role));
    }

    private Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct) =>
        _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);

    /// <summary>
    /// Only the hash is stored, so a leaked database doesn't leak usable tokens (same idea as password hashing).
    /// Plain SHA-256 is enough here, unlike passwords: the token is 512 random bits, so it can't be brute-forced.
    /// </summary>
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}