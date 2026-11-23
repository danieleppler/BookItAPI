namespace BookIt.Api.Dtos;

public record ServiceSummaryResponse(
    int ServiceId,
    string ServiceName,
    decimal Price,
    int UpcomingAppointments,
    decimal ExpectedRevenue);
