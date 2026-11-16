using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
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
}
