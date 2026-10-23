using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/leases/{leaseId:guid}/utility-services")]
[Authorize]
public class LeaseUtilityServicesController(ILeaseUtilityServiceService leaseUtilityServiceService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<LeaseUtilityServiceResponse>>>> GetAll(
        Guid leaseId, CancellationToken cancellationToken)
    {
        var services = await leaseUtilityServiceService.GetAllAsync(leaseId, cancellationToken);
        return Ok(new ApiResponse<List<LeaseUtilityServiceResponse>> { Data = services });
    }

    [HttpPut("{serviceId:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<LeaseUtilityServiceResponse>>> Upsert(
        Guid leaseId, Guid serviceId, UpsertLeaseUtilityServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await leaseUtilityServiceService.UpsertAsync(leaseId, serviceId, request, cancellationToken);
        return Ok(new ApiResponse<LeaseUtilityServiceResponse> { Data = service });
    }

    [HttpDelete("{serviceId:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Update)]
    public async Task<IActionResult> Remove(Guid leaseId, Guid serviceId, CancellationToken cancellationToken)
    {
        await leaseUtilityServiceService.RemoveAsync(leaseId, serviceId, cancellationToken);
        return NoContent();
    }
}
