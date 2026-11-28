using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Services.PaymentGateways.VnPay;

// VNPay payment gateway, API version 2.1.0.
// Docs: https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html (payment URL, IPN, response codes).
// Creating a payment calls nothing: the URL is signed here and the tenant's browser opens it.
public class VnPayGateway(IOptions<VnPayOptions> options) : IPaymentGateway
{
    public const string Version = "2.1.0";
    public const string SuccessCode = "00";

    // vnp_CreateDate / vnp_ExpireDate are Vietnam time (GMT+7), yyyyMMddHHmmss
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private const string DateFormat = "yyyyMMddHHmmss";
    private const string MaskedValue = "***";

    public PaymentGateway Gateway => PaymentGateway.VnPay;

    public static string TxnRefOf(Guid paymentId) => paymentId.ToString("N");

    public static string FormatDate(DateTimeOffset value) =>
        value.ToOffset(VietnamOffset).ToString(DateFormat, CultureInfo.InvariantCulture);

    // VNPay amounts are VND × 100, no decimal point
    public static string ToVnPayAmount(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100, 0)).ToString(CultureInfo.InvariantCulture);

    public Task<GatewayCheckout> CreateCheckoutAsync(
        GatewayCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = Version,
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = request.Credentials.MerchantCode,
            ["vnp_Amount"] = ToVnPayAmount(request.Amount),
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = TxnRefOf(request.PaymentId),
            ["vnp_OrderInfo"] = request.OrderInfo,
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = settings.Locale,
            ["vnp_ReturnUrl"] = settings.ReturnUrl,
            ["vnp_IpAddr"] = request.ClientIp,
            ["vnp_CreateDate"] = FormatDate(request.CreatedAt),
            ["vnp_ExpireDate"] = FormatDate(request.ExpiresAt)
        };

        var data = VnPaySignature.BuildData(fields);
        var hash = VnPaySignature.Sign(data, request.Credentials.Secret);
        var url = $"{settings.PaymentUrl}?{data}&{VnPaySignature.SecureHashField}={hash}";

        return Task.FromResult(new GatewayCheckout(url, ToJson(fields), ResponsePayload: null));
    }

    public GatewayCallbackData ParseCallback(GatewayCallback callback)
    {
        var fields = callback.Query;

        Guid? paymentId = fields.TryGetValue("vnp_TxnRef", out var txnRef) && Guid.TryParseExact(txnRef, "N", out var id)
            ? id
            : null;

        decimal? amount = fields.TryGetValue("vnp_Amount", out var rawAmount)
            && long.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var minorUnits)
                ? minorUnits / 100m
                : null;

        var isSuccess = fields.GetValueOrDefault("vnp_ResponseCode") == SuccessCode
            && fields.GetValueOrDefault("vnp_TransactionStatus") == SuccessCode;

        var transactionNo = fields.GetValueOrDefault("vnp_TransactionNo");

        return new GatewayCallbackData(
            paymentId,
            isSuccess && transactionNo is { Length: > 0 and <= PaymentTransaction.GatewayTransactionIdMaxLength }
                ? transactionNo
                : null,
            amount,
            isSuccess,
            ToJson(Masked(fields)));
    }

    public bool VerifyCallback(GatewayCallback callback, GatewayCredentials credentials) =>
        callback.Query.GetValueOrDefault("vnp_TmnCode") == credentials.MerchantCode
        && VnPaySignature.Verify(callback.Query, credentials.Secret);

    // VNPay IPN response codes: https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html#code-ipn-url
    public GatewayHttpResponse BuildCallbackResponse(GatewayCallbackOutcome outcome)
    {
        var (code, message) = outcome switch
        {
            GatewayCallbackOutcome.Received => ("00", "Confirm Success"),
            GatewayCallbackOutcome.AlreadyProcessed => ("02", "Order already confirmed"),
            GatewayCallbackOutcome.PaymentNotFound => ("01", "Order not found"),
            GatewayCallbackOutcome.InvalidAmount => ("04", "Invalid amount"),
            GatewayCallbackOutcome.InvalidSignature => ("97", "Invalid signature"),
            _ => ("99", "Unknown error")
        };

        return new GatewayHttpResponse(200, "application/json", JsonSerializer.Serialize(new { RspCode = code, Message = message }));
    }

    private static Dictionary<string, string> Masked(IReadOnlyDictionary<string, string> fields) =>
        fields.ToDictionary(
            f => f.Key,
            f => f.Key == VnPaySignature.SecureHashField ? MaskedValue : f.Value);

    private static string ToJson(IReadOnlyDictionary<string, string> fields) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string>(
            fields.ToDictionary(f => f.Key, f => f.Value), StringComparer.Ordinal));
}
