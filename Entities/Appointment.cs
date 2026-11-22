namespace BookIt.Api.Entities;

public class Appointment
{
    public int Id { get; set; }

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int ClientId { get; set; }
    public User Client { get; set; } = null!;

    public DateTime StartTime { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
}
