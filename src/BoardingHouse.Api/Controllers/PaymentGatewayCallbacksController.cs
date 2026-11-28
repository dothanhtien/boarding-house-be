using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/payment-gateways/{gateway}")]
[AllowAnonymous]
public class PaymentGatewayCallbacksController(IOnlinePaymentService onlinePaymentService) : ControllerBase
{
    // VNPay calls with GET + query string; MoMo / ZaloPay POST a JSON body
    [AcceptVerbs("GET", "POST", Route = "ipn")]
    public async Task<IActionResult> Ipn(PaymentGateway gateway, CancellationToken cancellationToken)
    {
        var query = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);

        string? body = null;
        if (HttpMethods.IsPost(Request.Method))
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var response = await onlinePaymentService.HandleCallbackAsync(
            gateway, new GatewayCallback(query, body), cancellationToken);

        return new ContentResult
        {
            StatusCode = response.StatusCode,
            ContentType = response.ContentType,
            Content = response.Body
        };
    }
}
