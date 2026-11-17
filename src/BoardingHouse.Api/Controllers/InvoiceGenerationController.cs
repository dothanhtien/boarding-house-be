using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Authorize]
public class InvoiceGenerationController(IInvoiceGenerationService invoiceGenerationService) : ControllerBase
{
    [HttpPost("api/properties/{propertyId:guid}/invoices/generate")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<InvoiceGenerationRunResponse>>> Generate(
        Guid propertyId, GenerateInvoicesRequest request, CancellationToken cancellationToken)
    {
        var run = await invoiceGenerationService.GenerateForPropertyAsync(propertyId, request, cancellationToken);
        return Ok(new ApiResponse<InvoiceGenerationRunResponse> { Data = run });
    }

    [HttpGet("api/invoice-generation-runs")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceGenerationRunResponse>>>> GetRuns(
    [FromQuery] InvoiceGenerationRunListQuery query, CancellationToken cancellationToken)
    {
        var runs = await invoiceGenerationService.GetRunsAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<InvoiceGenerationRunResponse>> { Data = runs });
    }

}
