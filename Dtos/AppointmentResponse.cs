using BookIt.Api.Entities;

namespace BookIt.Api.Dtos;

public record AppointmentResponse(
    int Id,
    int ServiceId,
    string ServiceName,
    int BusinessId,
    string BusinessName,
    DateTime StartTime,
    DateTime EndTime,
    AppointmentStatus Status);
