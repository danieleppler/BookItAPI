using BookIt.Api.Data;
using BookIt.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Services;

public class BusinessService : IBusinessService
{
    private const string LikeEscapeChar = "\\";

    private readonly BookItDbContext _db;

    public BusinessService(BookItDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<BusinessResponse>> GetBusinessesAsync(BusinessQuery query, CancellationToken ct = default)
    {
        var businesses = _db.Businesses.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            // ILIKE without wildcards = case-insensitive exact match
            var category = EscapeLike(query.Category.Trim());
            businesses = businesses.Where(b => EF.Functions.ILike(b.Category, category, LikeEscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            businesses = businesses.Where(b => EF.Functions.ILike(b.Name, pattern, LikeEscapeChar));
        }

        var totalCount = await businesses.CountAsync(ct);

        var items = await businesses
            .OrderBy(b => b.Name)
            .ThenBy(b => b.Id) // tie-breaker so paging is stable
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new BusinessResponse(b.Id, b.Name, b.Category))
            .ToListAsync(ct);

        return new PagedResult<BusinessResponse>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(LikeEscapeChar, LikeEscapeChar + LikeEscapeChar)
             .Replace("%", LikeEscapeChar + "%")
             .Replace("_", LikeEscapeChar + "_");
}
