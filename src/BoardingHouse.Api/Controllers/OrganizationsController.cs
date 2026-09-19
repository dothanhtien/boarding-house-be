using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/organizations")]
[Authorize]
public class OrganizationsController(
    IOrganizationService organizationService,
    IOrganizationSettingsService organizationSettingsService,
    IOrganizationMemberService organizationMemberService) : ControllerBase
{
    [HttpGet]
    [RequirePermission("organization", "read")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrganizationResponse>>>> GetAll(
        [FromQuery] OrganizationListQuery query,
        CancellationToken cancellationToken)
    {
        var organizations = await organizationService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<OrganizationResponse>> { Data = organizations });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("organization", "read")]
    public async Task<ActionResult<ApiResponse<OrganizationResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var organization = await organizationService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<OrganizationResponse> { Data = organization });
    }

    [HttpPost]
    [RequirePermission("organization", "create")]
    public async Task<ActionResult<ApiResponse<OrganizationResponse>>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organization = await organizationService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = organization.Id },
            new ApiResponse<OrganizationResponse> { Data = organization });
    }

    [HttpPatch("{id:guid}")]
    [RequirePermission("organization", "update")]
    public async Task<ActionResult<ApiResponse<OrganizationResponse>>> Update(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organization = await organizationService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<OrganizationResponse> { Data = organization });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("organization", "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await organizationService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/settings")]
    [RequirePermission("organization-setting", "read")]
    public async Task<ActionResult<ApiResponse<OrganizationSettingsResponse>>> GetSettings(Guid organizationId, CancellationToken cancellationToken)
    {
        var settings = await organizationSettingsService.GetByOrganizationIdAsync(organizationId, cancellationToken);
        return Ok(new ApiResponse<OrganizationSettingsResponse> { Data = settings });
    }

    [HttpPatch("{organizationId:guid}/settings")]
    [RequirePermission("organization-setting", "update")]
    public async Task<ActionResult<ApiResponse<OrganizationSettingsResponse>>> UpdateSettings(
        Guid organizationId, UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await organizationSettingsService.UpsertAsync(organizationId, request, cancellationToken);
        return Ok(new ApiResponse<OrganizationSettingsResponse> { Data = settings });
    }

    [HttpGet("{organizationId:guid}/members")]
    [RequirePermission("organization-member", "read")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrganizationMemberResponse>>>> GetMembers(
        Guid organizationId,
        [FromQuery] OrganizationMemberListQuery query,
        CancellationToken cancellationToken)
    {
        var members = await organizationMemberService.GetByOrganizationIdAsync(organizationId, query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<OrganizationMemberResponse>> { Data = members });
    }

    [HttpPost("{organizationId:guid}/members")]
    [RequirePermission("organization-member", "create")]
    public async Task<ActionResult<ApiResponse<OrganizationMemberResponse>>> AddMember(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken)
    {
        var member = await organizationMemberService.AddAsync(organizationId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new ApiResponse<OrganizationMemberResponse> { Data = member });
    }

    [HttpPut("{organizationId:guid}/members/{memberId:guid}")]
    [RequirePermission("organization-member", "update")]
    public async Task<ActionResult<ApiResponse<OrganizationMemberResponse>>> UpdateMemberRole(
    Guid organizationId, Guid memberId, UpdateOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await organizationMemberService.UpdateRoleAsync(organizationId, memberId, request, cancellationToken);
        return Ok(new ApiResponse<OrganizationMemberResponse> { Data = member });
    }

    [HttpDelete("{organizationId:guid}/members/{memberId:guid}")]
    [RequirePermission("organization-member", "delete")]
    public async Task<IActionResult> RemoveMember(Guid organizationId, Guid memberId, CancellationToken cancellationToken)
    {
        await organizationMemberService.RemoveAsync(organizationId, memberId, cancellationToken);
        return NoContent();
    }
}
