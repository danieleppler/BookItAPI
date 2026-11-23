using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public enum BusinessSummaryError
{
    None,
    BusinessNotFound,
    NotOwner,
}

public record BusinessSummaryResult(BusinessSummaryResponse? Summary, BusinessSummaryError Error)
{
    public static BusinessSummaryResult Success(BusinessSummaryResponse summary) => new(summary, BusinessSummaryError.None);
    public static BusinessSummaryResult Fail(BusinessSummaryError error) => new(null, error);
}
