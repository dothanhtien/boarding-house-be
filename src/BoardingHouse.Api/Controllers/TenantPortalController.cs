using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common.Listing;
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

    [HttpGet("leases")]
    public async Task<ActionResult<ApiResponse<PagedResult<MyLeaseResponse>>>> GetLeases(
        [FromQuery] MyLeaseListQuery query, CancellationToken cancellationToken)
    {
        var leases = await tenantPortalService.GetLeasesAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<MyLeaseResponse>> { Data = leases });
    }

    [HttpGet("leases/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MyLeaseDetailResponse>>> GetLease(Guid id, CancellationToken cancellationToken)
    {
        var lease = await tenantPortalService.GetLeaseAsync(id, cancellationToken);
        return Ok(new ApiResponse<MyLeaseDetailResponse> { Data = lease });
    }
}
