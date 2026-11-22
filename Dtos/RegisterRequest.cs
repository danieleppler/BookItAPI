using System.ComponentModel.DataAnnotations;
using BookIt.Api.Entities;

namespace BookIt.Api.Dtos;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; set; }
}
