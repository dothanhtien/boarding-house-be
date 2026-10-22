using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.TenantProfiles;
using BoardingHouse.Application.DTOs.Tenants;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController(ITenantService tenantService) : ControllerBase
{
    [HttpGet("lookup")]
    [RequireOrganizationScopedPermission(PermissionResources.Tenant, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<TenantLookupResponse>>> Lookup(
        [FromQuery] TenantLookupQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenantService.LookupAsync(query, cancellationToken);
        return Ok(new ApiResponse<TenantLookupResponse> { Data = tenant });
    }

    [HttpGet("{userId:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Tenant, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<TenantResponse>>> GetById(Guid userId, CancellationToken cancellationToken)
    {
        var tenant = await tenantService.GetByIdAsync(userId, cancellationToken);
        return Ok(new ApiResponse<TenantResponse> { Data = tenant });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission(PermissionResources.Tenant, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<TenantResponse>>> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await tenantService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { userId = tenant.Id },
            new ApiResponse<TenantResponse> { Data = tenant });
    }

    [HttpPatch("{userId:guid}/profile")]
    [RequireOrganizationScopedPermission(PermissionResources.Tenant, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<TenantProfileResponse>>> UpdateProfile(
        Guid userId, UpdateTenantProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await tenantService.UpdateProfileAsync(userId, request, cancellationToken);
        return Ok(new ApiResponse<TenantProfileResponse> { Data = profile });
    }
}
