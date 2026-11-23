using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public interface IAppointmentService
{
    /// <summary>Books a service for the given client. Fails if the service doesn't exist, the time is invalid, or the slot overlaps an existing booking.</summary>
    Task<BookingResult> BookAsync(int clientId, CreateAppointmentRequest request, CancellationToken ct = default);
}
