using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.UtilityServices;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/utility-services")]
[Authorize]
public class UtilityServicesController(IUtilityServiceService utilityServiceService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission("utility-service", "read")]
    public async Task<ActionResult<ApiResponse<List<UtilityServiceResponse>>>> GetAll([FromQuery] UtilityServiceListQuery query, CancellationToken cancellationToken)
    {
        var services = await utilityServiceService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<List<UtilityServiceResponse>> { Data = services });
    }

    [HttpGet("{id:guid}")]
    [RequireOrganizationScopedPermission("utility-service", "read")]
    public async Task<ActionResult<ApiResponse<UtilityServiceResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var service = await utilityServiceService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<UtilityServiceResponse> { Data = service });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission("utility-service", "create")]
    public async Task<ActionResult<ApiResponse<UtilityServiceResponse>>> Create(
        CreateUtilityServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await utilityServiceService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = service.Id },
            new ApiResponse<UtilityServiceResponse> { Data = service });
    }

    [HttpPatch("{id:guid}")]
    [RequireOrganizationScopedPermission("utility-service", "update")]
    public async Task<ActionResult<ApiResponse<UtilityServiceResponse>>> Update(Guid id, UpdateUtilityServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await utilityServiceService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<UtilityServiceResponse> { Data = service });
    }

    [HttpDelete("{id:guid}")]
    [RequireOrganizationScopedPermission("utility-service", "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await utilityServiceService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
