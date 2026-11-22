using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Dtos;

public class BusinessQuery
{
    // Capped so (Page - 1) * PageSize can't overflow int
    [Range(1, 100_000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    /// <summary>Exact category match, case-insensitive.</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Free-text search on business name (contains, case-insensitive).</summary>
    [MaxLength(200)]
    public string? Search { get; set; }
}
