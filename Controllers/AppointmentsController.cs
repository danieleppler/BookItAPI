using System.Security.Claims;
using BookIt.Api.Dtos;
using BookIt.Api.Entities;
using BookIt.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentsController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Client))]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> Book(CreateAppointmentRequest request, CancellationToken ct)
    {
        // Caller identity always comes from the signed token, never from the request body.
        // The JWT "sub" claim is mapped to NameIdentifier by the default inbound claim mapping.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var clientId))
        {
            return Unauthorized();
        }

        var result = await _appointmentService.BookAsync(clientId, request, ct);

        return result.Error switch
        {
            BookingError.None => StatusCode(StatusCodes.Status201Created, result.Appointment),
            BookingError.ServiceNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Service not found"),
            BookingError.InvalidStartTime => Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Start time must be in the future and within one year"),
            BookingError.SlotTaken => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This time slot overlaps an existing booking for the service"),
            _ => throw new InvalidOperationException($"Unhandled booking error {result.Error}"),
        };
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AppointmentResponse>>> GetMine(
        [FromQuery] AppointmentQuery query, CancellationToken ct)
    {
        // Both user id and role come from the signed token, so a caller can only ever see their own scope
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), out var role) ||
            !Enum.IsDefined(role))
        {
            return Unauthorized();
        }

        return Ok(await _appointmentService.GetForUserAsync(userId, role, query, ct));
    }
}
