using BookIt.Api.Dtos;
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
}
