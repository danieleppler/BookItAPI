namespace BookIt.Api.Dtos;

public record BusinessSummaryResponse(
    int BusinessId,
    string BusinessName,
    int TotalUpcomingAppointments,
    decimal TotalExpectedRevenue,
    IReadOnlyList<ServiceSummaryResponse> Services);
