namespace BookIt.Api.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public ICollection<Business> OwnedBusinesses { get; set; } = new List<Business>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
