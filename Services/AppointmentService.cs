using BookIt.Api.Data;
using BookIt.Api.Dtos;
using BookIt.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookIt.Api.Services;

public class AppointmentService : IAppointmentService
{
    private static readonly TimeSpan MaxBookingHorizon = TimeSpan.FromDays(365);

    private readonly BookItDbContext _db;

    public AppointmentService(BookItDbContext db)
    {
        _db = db;
    }

    public async Task<BookingResult> BookAsync(int clientId, CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var serviceId = request.ServiceId!.Value;
        var start = request.StartTime!.Value.UtcDateTime;
        var now = DateTime.UtcNow;

        // Upper bound also guards against DateTime overflow when adding the duration
        if (start <= now || start > now + MaxBookingHorizon)
        {
            return BookingResult.Fail(BookingError.InvalidStartTime);
        }

        var service = await _db.Services
            .AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Select(s => new { s.Id, s.Name, s.DurationMinutes, s.BusinessId, BusinessName = s.Business.Name })
            .SingleOrDefaultAsync(ct);

        if (service is null)
        {
            return BookingResult.Fail(BookingError.ServiceNotFound);
        }

        var appointment = new Appointment
        {
            ServiceId = service.Id,
            ClientId = clientId,
            StartTime = start,
            EndTime = start.AddMinutes(service.DurationMinutes),
            Status = AppointmentStatus.Pending, // new bookings await owner confirmation; Pending still reserves the slot
        };
        _db.Appointments.Add(appointment);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: "EX_Appointments_NoOverlap",
        })
        {
            // The DB rejected an overlapping booking; this holds even under concurrent requests
            return BookingResult.Fail(BookingError.SlotTaken);
        }

        return BookingResult.Success(new AppointmentResponse(
            appointment.Id,
            service.Id,
            service.Name,
            service.BusinessId,
            service.BusinessName,
            appointment.StartTime,
            appointment.EndTime,
            appointment.Status));
    }

    public async Task<IReadOnlyList<AppointmentResponse>> GetForUserAsync(
        int userId, UserRole role, AppointmentQuery query, CancellationToken ct = default)
    {
        // Ownership scoping is applied first and unconditionally, so filters can only narrow the caller's own data
        var appointments = role switch
        {
            UserRole.Client => _db.Appointments.Where(a => a.ClientId == userId),
            UserRole.BusinessOwner => _db.Appointments.Where(a => a.Service.Business.OwnerId == userId),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported role"),
        };

        // Overlap semantics: anything that intersects [from, to), including appointments in progress at "from"
        if (query.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            appointments = appointments.Where(a => a.EndTime > fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            appointments = appointments.Where(a => a.StartTime < toUtc);
        }

        if (query.Status is { } status)
        {
            appointments = appointments.Where(a => a.Status == status);
        }

        // Projecting straight into the DTO makes EF emit a single SELECT with JOINs to Services and Businesses:
        // no entities are materialized or tracked, and there are no per-row lazy loads (no N+1).
        return await appointments
            .OrderBy(a => a.StartTime)
            .ThenBy(a => a.Id)
            .Select(a => new AppointmentResponse(
                a.Id,
                a.ServiceId,
                a.Service.Name,
                a.Service.BusinessId,
                a.Service.Business.Name,
                a.StartTime,
                a.EndTime,
                a.Status))
            .ToListAsync(ct);
    }
}
