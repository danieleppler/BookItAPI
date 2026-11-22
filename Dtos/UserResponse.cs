using BookIt.Api.Entities;

namespace BookIt.Api.Dtos;

public record UserResponse(int Id, string Email, string FullName, UserRole Role);
