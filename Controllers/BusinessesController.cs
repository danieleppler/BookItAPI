using System.Security.Claims;
using BookIt.Api.Dtos;
using BookIt.Api.Entities;
using BookIt.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/businesses")]
public class BusinessesController : ControllerBase
{
    private readonly IBusinessService _businessService;

    public BusinessesController(IBusinessService businessService)
    {
        _businessService = businessService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BusinessResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<BusinessResponse>>> GetBusinesses(
        [FromQuery] BusinessQuery query, CancellationToken ct)
    {
        return Ok(await _businessService.GetBusinessesAsync(query, ct));
    }

    [HttpGet("{id:int}/summary")]
    [Authorize(Roles = nameof(UserRole.BusinessOwner))] // Clients get 403 here; owners of other businesses get 403 below
    [ProducesResponseType(typeof(BusinessSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BusinessSummaryResponse>> GetSummary(int id, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
        {
            return Unauthorized();
        }

        var result = await _businessService.GetSummaryAsync(id, ownerId, ct);

        return result.Error switch
        {
            BusinessSummaryError.None => Ok(result.Summary),
            BusinessSummaryError.BusinessNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Business not found"),
            BusinessSummaryError.NotOwner => Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Only the owner of this business can view its summary"),
            _ => throw new InvalidOperationException($"Unhandled summary error {result.Error}"),
        };
    }
}
