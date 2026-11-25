using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class TenantPortalController(ITenantPortalService tenantPortalService) : ControllerBase
{
    [HttpGet("tenancy")]
    public async Task<ActionResult<ApiResponse<TenancyResponse>>> GetTenancy(CancellationToken cancellationToken)
    {
        var tenancy = await tenantPortalService.GetTenancyAsync(cancellationToken);
        return Ok(new ApiResponse<TenancyResponse> { Data = tenancy });
    }
}
