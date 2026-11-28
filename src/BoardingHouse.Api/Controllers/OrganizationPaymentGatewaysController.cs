using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:guid}/payment-gateways")]
[Authorize]
public class OrganizationPaymentGatewaysController(
    IOrganizationPaymentGatewayService organizationPaymentGatewayService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission(PermissionResources.OrganizationSetting, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<OrganizationPaymentGatewayResponse>>>> GetAll(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var accounts = await organizationPaymentGatewayService.ListAsync(organizationId, cancellationToken);
        return Ok(new ApiResponse<List<OrganizationPaymentGatewayResponse>> { Data = accounts });
    }

    [HttpPut("{gateway}")]
    [RequireOrganizationScopedPermission(PermissionResources.OrganizationSetting, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<OrganizationPaymentGatewayResponse>>> Upsert(
        Guid organizationId,
        PaymentGateway gateway,
        UpsertOrganizationPaymentGatewayRequest request,
        CancellationToken cancellationToken)
    {
        var account = await organizationPaymentGatewayService.UpsertAsync(organizationId, gateway, request, cancellationToken);
        return Ok(new ApiResponse<OrganizationPaymentGatewayResponse> { Data = account });
    }
}
