using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/properties")]
[Authorize]
public class PropertiesController(IPropertyService propertyService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission("property", "read")]
    public async Task<ActionResult<ApiResponse<PagedResult<PropertyResponse>>>> GetAll(
        [FromQuery] PropertyListQuery query, CancellationToken cancellationToken)
    {
        var properties = await propertyService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<PropertyResponse>> { Data = properties });
    }

    [HttpGet("{id:guid}")]
    [RequireOrganizationScopedPermission("property", "read")]
    public async Task<ActionResult<ApiResponse<PropertyResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var property = await propertyService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<PropertyResponse> { Data = property });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission("property", "create")]
    public async Task<ActionResult<ApiResponse<PropertyResponse>>> Create(CreatePropertyRequest request, CancellationToken cancellationToken)
    {
        var property = await propertyService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = property.Id },
            new ApiResponse<PropertyResponse> { Data = property });
    }

    [HttpPatch("{id:guid}")]
    [RequireOrganizationScopedPermission("property", "update")]
    public async Task<ActionResult<ApiResponse<PropertyResponse>>> Update(
        Guid id, UpdatePropertyRequest request, CancellationToken cancellationToken)
    {
        var property = await propertyService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<PropertyResponse> { Data = property });
    }

    [HttpDelete("{id:guid}")]
    [RequireOrganizationScopedPermission("property", "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await propertyService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
