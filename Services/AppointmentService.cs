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
}
