using System.ComponentModel.DataAnnotations;
using BookIt.Api.Entities;

namespace BookIt.Api.Dtos;

public class AppointmentQuery : IValidatableObject
{
    /// <summary>Inclusive lower bound. Returns appointments still running at or after this time (EndTime &gt; from). ISO 8601 with offset.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Exclusive upper bound. Returns appointments starting before this time (StartTime &lt; to). ISO 8601 with offset.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Pending, Confirmed or Cancelled (case-insensitive).</summary>
    public AppointmentStatus? Status { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is not null && To is not null && From >= To)
        {
            yield return new ValidationResult("'from' must be earlier than 'to'.", new[] { nameof(From), nameof(To) });
        }
    }
}
