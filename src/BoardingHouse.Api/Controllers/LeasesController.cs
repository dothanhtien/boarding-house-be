using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/leases")]
[Authorize]
public class LeasesController(ILeaseService leaseService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaseResponse>>>> GetAll(
        [FromQuery] LeaseListQuery query, CancellationToken cancellationToken)
    {
        var leases = await leaseService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<LeaseResponse>> { Data = leases });
    }

    [HttpGet("{id:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<LeaseResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var lease = await leaseService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<LeaseResponse> { Data = lease });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<LeaseResponse>>> Create(CreateLeaseRequest request, CancellationToken cancellationToken)
    {
        var lease = await leaseService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = lease.Id },
            new ApiResponse<LeaseResponse> { Data = lease });
    }

    [HttpPatch("{id:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<LeaseResponse>>> Update(Guid id, UpdateLeaseRequest request, CancellationToken cancellationToken)
    {
        var lease = await leaseService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<LeaseResponse> { Data = lease });
    }

    [HttpDelete("{id:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await leaseService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Activate)]
    public async Task<ActionResult<ApiResponse<LeaseResponse>>> Activate(Guid id, CancellationToken cancellationToken)
    {
        var lease = await leaseService.ActivateAsync(id, cancellationToken);
        return Ok(new ApiResponse<LeaseResponse> { Data = lease });
    }

    [HttpPost("{id:guid}/terminate")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Terminate)]
    public async Task<ActionResult<ApiResponse<LeaseResponse>>> Terminate(
        Guid id, TerminateLeaseRequest request, CancellationToken cancellationToken)
    {
        var lease = await leaseService.TerminateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<LeaseResponse> { Data = lease });
    }

    [HttpGet("{id:guid}/status-histories")]
    [RequireOrganizationScopedPermission(PermissionResources.Lease, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<LeaseStatusHistoryResponse>>>> GetStatusHistories(
        Guid id, CancellationToken cancellationToken)
    {
        var histories = await leaseService.GetStatusHistoriesAsync(id, cancellationToken);
        return Ok(new ApiResponse<List<LeaseStatusHistoryResponse>> { Data = histories });
    }
}
