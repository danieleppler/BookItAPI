using BookIt.Api.Dtos;
using BookIt.Api.Entities;

namespace BookIt.Api.Services;

public interface IAppointmentService
{
    /// <summary>Books a service for the given client. Fails if the service doesn't exist, the time is invalid, or the slot overlaps an existing booking.</summary>
    Task<BookingResult> BookAsync(int clientId, CreateAppointmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lists the caller's appointments: a Client gets their own bookings, a BusinessOwner gets bookings
    /// for services of businesses they own. Ordered by start time.
    /// </summary>
    Task<IReadOnlyList<AppointmentResponse>> GetForUserAsync(int userId, UserRole role, AppointmentQuery query, CancellationToken ct = default);
}
