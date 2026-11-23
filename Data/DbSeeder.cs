using BookIt.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Data;

public static class DbSeeder
{
    public const string SeedPassword = "Password123!";

    private static readonly string[] Categories =
        { "Barber", "Physiotherapy", "Tutoring", "Fitness", "Beauty", "Massage" };

    private static readonly Dictionary<string, string[]> ServiceNames = new()
    {
        ["Barber"] = new[] { "Haircut", "Beard Trim", "Hot Towel Shave" },
        ["Physiotherapy"] = new[] { "Initial Assessment", "Follow-up Session", "Sports Rehab" },
        ["Tutoring"] = new[] { "Math Lesson", "English Lesson", "Exam Prep" },
        ["Fitness"] = new[] { "Personal Training", "Group Class", "Nutrition Consult" },
        ["Beauty"] = new[] { "Manicure", "Facial", "Makeup Session" },
        ["Massage"] = new[] { "Swedish Massage", "Deep Tissue", "Hot Stone" },
    };

    public static async Task SeedAsync(BookItDbContext db, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var rng = new Random(42);
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(SeedPassword);

        var owners = Enumerable.Range(1, 4).Select(i => new User
        {
            Email = $"owner{i}@bookit.com",
            FullName = $"Owner {i}",
            PasswordHash = passwordHash,
            Role = UserRole.BusinessOwner,
        }).ToList();

        var clients = Enumerable.Range(1, 6).Select(i => new User
        {
            Email = $"client{i}@bookit.com",
            FullName = $"Client {i}",
            PasswordHash = passwordHash,
            Role = UserRole.Client,
        }).ToList();

        // 20 businesses, 10 with 3 services and 10 with 2 = 50 services
        var businesses = new List<Business>();
        for (var i = 0; i < 20; i++)
        {
            var category = Categories[i % Categories.Length];
            var business = new Business
            {
                Name = $"{category} Studio {i + 1}",
                Category = category,
                Owner = owners[i % owners.Count],
            };

            var serviceCount = i < 10 ? 3 : 2;
            for (var s = 0; s < serviceCount; s++)
            {
                business.Services.Add(new Service
                {
                    Name = ServiceNames[category][s],
                    DurationMinutes = new[] { 30, 45, 60, 90 }[rng.Next(4)],
                    Price = rng.Next(20, 200),
                });
            }

            businesses.Add(business);
        }

        var services = businesses.SelectMany(b => b.Services).ToList();
        var statuses = Enum.GetValues<AppointmentStatus>();
        var today = DateTime.UtcNow.Date;

        // 200 appointments spread over 30 days in the past and 30 days ahead.
        // A random slot that overlaps an earlier active booking of the same service is seeded as Cancelled,
        // so the data satisfies the EX_Appointments_NoOverlap constraint.
        var appointments = new List<Appointment>();
        for (var i = 0; i < 200; i++)
        {
            var service = services[rng.Next(services.Count)];
            var start = today.AddDays(rng.Next(-30, 31)).AddHours(rng.Next(9, 18));
            var end = start.AddMinutes(service.DurationMinutes);
            var status = statuses[rng.Next(statuses.Length)];

            var overlaps = appointments.Any(a =>
                a.Service == service &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartTime < end && start < a.EndTime);
            if (overlaps)
            {
                status = AppointmentStatus.Cancelled;
            }

            appointments.Add(new Appointment
            {
                Service = service,
                Client = clients[rng.Next(clients.Count)],
                StartTime = start,
                EndTime = end,
                Status = status,
            });
        }

        db.Users.AddRange(owners);
        db.Users.AddRange(clients);
        db.Businesses.AddRange(businesses);
        db.Appointments.AddRange(appointments);
        await db.SaveChangesAsync(ct);
    }
}
