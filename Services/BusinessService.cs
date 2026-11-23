using BookIt.Api.Data;
using BookIt.Api.Dtos;
using BookIt.Api.Entities;
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

    public async Task<BusinessSummaryResult> GetSummaryAsync(int businessId, int ownerId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // A single round trip: the business row (for the ownership check) LEFT JOINed to its services, where each
        // service carries a correlated COUNT(*) subquery over its appointments (served by the (ServiceId, StartTime)
        // index). Starting from Services rather than grouping Appointments means services with no upcoming
        // bookings still appear, with 0. "Upcoming" = future and non-cancelled, so both Pending and Confirmed count.
        // The counts are computed even when the caller turns out not to be the owner; that's cheaper than a
        // second round trip on the normal path, and nothing is returned to a non-owner.
        var business = await _db.Businesses
            .Where(b => b.Id == businessId)
            .Select(b => new
            {
                b.Name,
                b.OwnerId,
                Services = b.Services
                    .OrderBy(s => s.Name)
                    .ThenBy(s => s.Id)
                    .Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.Price,
                        UpcomingCount = s.Appointments.Count(a =>
                            a.StartTime > now && a.Status != AppointmentStatus.Cancelled),
                    })
                    .ToList(),
            })
            .SingleOrDefaultAsync(ct);

        if (business is null)
        {
            return BusinessSummaryResult.Fail(BusinessSummaryError.BusinessNotFound);
        }

        if (business.OwnerId != ownerId)
        {
            return BusinessSummaryResult.Fail(BusinessSummaryError.NotOwner);
        }

        // count × price is done in memory on the already-aggregated rows (one per service), with exact decimal math
        var items = business.Services
            .Select(s => new ServiceSummaryResponse(s.Id, s.Name, s.Price, s.UpcomingCount, s.UpcomingCount * s.Price))
            .ToList();

        return BusinessSummaryResult.Success(new BusinessSummaryResponse(
            businessId,
            business.Name,
            items.Sum(i => i.UpcomingAppointments),
            items.Sum(i => i.ExpectedRevenue),
            items));
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(LikeEscapeChar, LikeEscapeChar + LikeEscapeChar)
             .Replace("%", LikeEscapeChar + "%")
             .Replace("_", LikeEscapeChar + "_");
}
