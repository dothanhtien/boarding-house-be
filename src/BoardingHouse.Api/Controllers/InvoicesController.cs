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
[Route("api/invoices")]
[Authorize]
public class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpGet]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceResponse>>>> GetAll(
        [FromQuery] InvoiceListQuery query, CancellationToken cancellationToken)
    {
        var invoices = await invoiceService.GetAllAsync(query, cancellationToken);
        return Ok(new ApiResponse<PagedResult<InvoiceResponse>> { Data = invoices });
    }

    [HttpGet("{id:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<InvoiceDetailResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<InvoiceDetailResponse> { Data = invoice });
    }

    [HttpPost("preview")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<InvoicePreviewResponse>>> Preview(
        CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var preview = await invoiceService.PreviewAsync(request, cancellationToken);
        return Ok(new ApiResponse<InvoicePreviewResponse> { Data = preview });
    }

    [HttpPost]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<InvoiceDetailResponse>>> Create(
        CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, new ApiResponse<InvoiceDetailResponse> { Data = invoice });
    }

    [HttpPatch("{id:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Update)]
    public async Task<ActionResult<ApiResponse<InvoiceDetailResponse>>> Update(
        Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.UpdateAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<InvoiceDetailResponse> { Data = invoice });
    }

    [HttpPost("{id:guid}/cancel")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Cancel)]
    public async Task<ActionResult<ApiResponse<InvoiceDetailResponse>>> Cancel(
        Guid id, CancelInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.CancelAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<InvoiceDetailResponse> { Data = invoice });
    }

    [HttpGet("{id:guid}/status-histories")]
    [RequireOrganizationScopedPermission(PermissionResources.Invoice, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<InvoiceStatusHistoryResponse>>>> GetStatusHistories(
        Guid id, CancellationToken cancellationToken)
    {
        var histories = await invoiceService.GetStatusHistoriesAsync(id, cancellationToken);
        return Ok(new ApiResponse<List<InvoiceStatusHistoryResponse>> { Data = histories });
    }
}
