namespace BookIt.Api.Dtos;

public record LoginResponse(string Token, DateTime ExpiresAtUtc, UserResponse User);
