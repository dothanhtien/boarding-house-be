using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/auth/me/tenant-profile")]
[Authorize]
public class TenantProfileController(
    ITenantProfileService tenantProfileService,
    ICurrentUserAccessor currentUserAccessor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<TenantProfileResponse?>>> Get(CancellationToken cancellationToken)
    {
        var profile = await tenantProfileService.GetByUserIdAsync(currentUserAccessor.RequiredUser.Id, cancellationToken);
        return Ok(new ApiResponse<TenantProfileResponse?> { Data = profile });
    }

    [HttpPatch]
    public async Task<ActionResult<ApiResponse<TenantProfileResponse>>> Update(UpdateTenantProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await tenantProfileService.UpsertAsync(currentUserAccessor.RequiredUser.Id, request, cancellationToken);
        return Ok(new ApiResponse<TenantProfileResponse> { Data = profile });
    }
}
