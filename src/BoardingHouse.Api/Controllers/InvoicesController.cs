using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpPost("preview")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<InvoicePreviewResponse>>> Preview(
        CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var preview = await invoiceService.PreviewAsync(request, cancellationToken);
        return Ok(new ApiResponse<InvoicePreviewResponse> { Data = preview });
    }
}
