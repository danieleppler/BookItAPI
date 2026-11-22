using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Dtos;

public class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}
