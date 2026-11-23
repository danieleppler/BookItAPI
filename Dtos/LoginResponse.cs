namespace BookIt.Api.Dtos;

/// <summary>Returned by both login and refresh: a short-lived access token plus a new single-use refresh token.</summary>
public record LoginResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserResponse User);
