using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public interface IBusinessService
{
    Task<PagedResult<BusinessResponse>> GetBusinessesAsync(BusinessQuery query, CancellationToken ct = default);

    /// <summary>
    /// Per-service count of upcoming (future, non-cancelled) appointments and expected revenue (count × price).
    /// Fails if the business doesn't exist or isn't owned by <paramref name="ownerId"/>.
    /// </summary>
    Task<BusinessSummaryResult> GetSummaryAsync(int businessId, int ownerId, CancellationToken ct = default);
}
