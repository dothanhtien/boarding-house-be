using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Authorize]
public class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpGet("api/invoices/{invoiceId:guid}/payments")]
    [RequireOrganizationScopedPermission(PermissionResources.Payment, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<List<PaymentResponse>>>> GetByInvoice(
        Guid invoiceId, CancellationToken cancellationToken)
    {
        var payments = await paymentService.GetByInvoiceAsync(invoiceId, cancellationToken);
        return Ok(new ApiResponse<List<PaymentResponse>> { Data = payments });
    }

    [HttpGet("api/payments/{id:guid}")]
    [RequireOrganizationScopedPermission(PermissionResources.Payment, PermissionActions.Read)]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var payment = await paymentService.GetByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<PaymentResponse> { Data = payment });
    }

    [HttpPost("api/invoices/{invoiceId:guid}/payments")]
    [RequireOrganizationScopedPermission(PermissionResources.Payment, PermissionActions.Create)]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> Record(
        Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await paymentService.RecordAsync(invoiceId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, new ApiResponse<PaymentResponse> { Data = payment });
    }

    [HttpPost("api/payments/{id:guid}/confirm")]
    [RequireOrganizationScopedPermission(PermissionResources.Payment, PermissionActions.Confirm)]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var payment = await paymentService.ConfirmAsync(id, cancellationToken);
        return Ok(new ApiResponse<PaymentResponse> { Data = payment });
    }

    [HttpPost("api/payments/{id:guid}/void")]
    [RequireOrganizationScopedPermission(PermissionResources.Payment, PermissionActions.Void)]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> Void(
        Guid id, VoidPaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await paymentService.VoidAsync(id, request, cancellationToken);
        return Ok(new ApiResponse<PaymentResponse> { Data = payment });
    }
}
