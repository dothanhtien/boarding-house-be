using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class TenantPortalController(
    ITenantPortalService tenantPortalService,
    IVietQrService vietQrService) : ControllerBase
{
    [HttpGet("tenancy")]
    public async Task<ActionResult<ApiResponse<TenancyResponse>>> GetTenancy(CancellationToken cancellationToken)
    {
        var tenancy = await tenantPortalService.GetTenancyAsync(cancellationToken);
        return Ok(new ApiResponse<TenancyResponse> { Data = tenancy });
    }

    [HttpGet("leases")]
    public async Task<ActionResult<ApiResponse<PagedResult<MyLeaseResponse>>>> GetLeases(
        [FromQuery] MyLeaseListQuery query, CancellationToken cancellationToken)
    {
        var leases = await tenantPortalService.GetLeasesAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<MyLeaseResponse>> { Data = leases });
    }

    [HttpGet("leases/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MyLeaseDetailResponse>>> GetLease(Guid id, CancellationToken cancellationToken)
    {
        var lease = await tenantPortalService.GetLeaseAsync(id, cancellationToken);
        return Ok(new ApiResponse<MyLeaseDetailResponse> { Data = lease });
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<ApiResponse<PagedResult<MyInvoiceResponse>>>> GetInvoices(
    [FromQuery] MyInvoiceListQuery query, CancellationToken cancellationToken)
    {
        var invoices = await tenantPortalService.GetInvoicesAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<MyInvoiceResponse>> { Data = invoices });
    }

    [HttpGet("invoices/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MyInvoiceDetailResponse>>> GetInvoice(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await tenantPortalService.GetInvoiceAsync(id, cancellationToken);
        return Ok(new ApiResponse<MyInvoiceDetailResponse> { Data = invoice });
    }

    [HttpGet("invoices/{id:guid}/payment-qr")]
    public async Task<ActionResult<ApiResponse<InvoicePaymentQrResponse>>> GetInvoicePaymentQr(
        Guid id, CancellationToken cancellationToken)
    {
        var qr = await vietQrService.GetMyInvoicePaymentQrAsync(id, cancellationToken);
        return Ok(new ApiResponse<InvoicePaymentQrResponse> { Data = qr });
    }

    [HttpGet("leases/{id:guid}/utility-readings")]
    public async Task<ActionResult<ApiResponse<PagedResult<MyUtilityReadingResponse>>>> GetUtilityReadings(
    Guid id, [FromQuery] MyUtilityReadingListQuery query, CancellationToken cancellationToken)
    {
        var readings = await tenantPortalService.GetUtilityReadingsAsync(id, query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<MyUtilityReadingResponse>> { Data = readings });
    }

    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<PagedResult<MyPaymentResponse>>>> GetPayments(
        [FromQuery] MyPaymentListQuery query, CancellationToken cancellationToken)
    {
        var payments = await tenantPortalService.GetPaymentsAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<MyPaymentResponse>> { Data = payments });
    }
}
