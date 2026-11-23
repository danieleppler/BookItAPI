using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public enum BookingError
{
    None,
    ServiceNotFound,
    InvalidStartTime,
    SlotTaken,
}

public record BookingResult(AppointmentResponse? Appointment, BookingError Error)
{
    public static BookingResult Success(AppointmentResponse appointment) => new(appointment, BookingError.None);
    public static BookingResult Fail(BookingError error) => new(null, error);
}
