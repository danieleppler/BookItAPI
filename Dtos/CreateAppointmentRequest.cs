using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Dtos;

public class CreateAppointmentRequest
{
    // Nullable + [Required] so a missing field is a 400, not silently 0 / default date
    [Required, Range(1, int.MaxValue)]
    public int? ServiceId { get; set; }

    /// <summary>ISO 8601 with offset, e.g. 2026-11-25T10:00:00Z.</summary>
    [Required]
    public DateTimeOffset? StartTime { get; set; }
}
