using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Authorize]
public class VietQrController(IVietQrService vietQrService) : ControllerBase
{
    [HttpGet("api/vietqr/banks")]
    public ActionResult<ApiResponse<List<VietQrBankResponse>>> GetBanks() =>
        Ok(new ApiResponse<List<VietQrBankResponse>> { Data = vietQrService.GetBanks() });

    [HttpGet("api/invoices/{invoiceId:guid}/payment-qr")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<InvoicePaymentQrResponse>>> GetInvoicePaymentQr(
        Guid invoiceId, CancellationToken cancellationToken)
    {
        var qr = await vietQrService.GetInvoicePaymentQrAsync(invoiceId, cancellationToken);
        return Ok(new ApiResponse<InvoicePaymentQrResponse> { Data = qr });
    }
}
