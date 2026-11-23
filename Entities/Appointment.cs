namespace BookIt.Api.Entities;

public class Appointment
{
    public int Id { get; set; }

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int ClientId { get; set; }
    public User Client { get; set; } = null!;

    public DateTime StartTime { get; set; }

    /// <summary>StartTime + service duration, stored so the DB can enforce no overlapping bookings.</summary>
    public DateTime EndTime { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
}
