using BookIt.Api.Dtos;

namespace BookIt.Api.Services;

public interface IBusinessService
{
    Task<PagedResult<BusinessResponse>> GetBusinessesAsync(BusinessQuery query, CancellationToken ct = default);
}
