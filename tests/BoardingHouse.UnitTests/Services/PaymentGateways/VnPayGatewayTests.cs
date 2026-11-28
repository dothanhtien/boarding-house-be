using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BoardingHouse.Application.Services.PaymentGateways;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Services.PaymentGateways.VnPay;

namespace BoardingHouse.UnitTests.Services.PaymentGateways;

public class VnPayGatewayTests
{
    private const string Secret = "TESTSECRET0123456789";
    private const string MerchantCode = "TESTCODE";
    private static readonly Guid PaymentId = Guid.Parse("6f1c0b9a-2d4e-4f7a-9b3c-1d2e3f405162");
    private static readonly Guid InvoiceId = Guid.Parse("3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f");
    private const string ReturnUrl = "http://localhost:3000/tenant-portal/payments/vnpay-return";

    // vnp_ReturnUrl carries ?invoiceId=..., encoded inside the signed value (? = %3F, = = %3D)
    private const string ExpectedData =
        "vnp_Amount=300000000&vnp_Command=pay&vnp_CreateDate=20261127103000&vnp_CurrCode=VND"
        + "&vnp_ExpireDate=20261127104500&vnp_IpAddr=127.0.0.1&vnp_Locale=vn&vnp_OrderInfo=Invoice+INV-202611-0001"
        + "&vnp_OrderType=other"
        + "&vnp_ReturnUrl=http%3A%2F%2Flocalhost%3A3000%2Ftenant-portal%2Fpayments%2Fvnpay-return"
        + "%3FinvoiceId%3D3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f"
        + "&vnp_TmnCode=TESTCODE&vnp_TxnRef=6f1c0b9a2d4e4f7a9b3c1d2e3f405162&vnp_Version=2.1.0";

    private const string ExpectedHash =
        "a2dcdd6629cd5f50cd12279007ac3fd98c13097bbf14cdee5a2ee9e285a1e800"
        + "21d9cbcf14caf4530b3ed8f145ec1cb45bee001be68094048afaf052f853c56d";

    private static readonly GatewayCredentials Credentials = new(MerchantCode, Secret);

    private readonly VnPayGateway _gateway = Gateway(ReturnUrl);

    private static VnPayGateway Gateway(string returnUrl) => new(Options.Create(new VnPayOptions
    {
        PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
        ReturnUrl = returnUrl,
        Locale = "vn"
    }));

    // 10:30 and 10:45 in Vietnam
    private static GatewayCheckoutRequest CheckoutRequest() => new(
        PaymentId,
        InvoiceId,
        3_000_000m,
        "Invoice INV-202611-0001",
        "127.0.0.1",
        new DateTimeOffset(2026, 11, 27, 3, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 27, 3, 45, 0, TimeSpan.Zero),
        Credentials);

    private static Dictionary<string, string> IpnFields(
        string responseCode = "00", string transactionStatus = "00", string amount = "300000000") => new()
    {
        ["vnp_Amount"] = amount,
        ["vnp_BankCode"] = "NCB",
        ["vnp_OrderInfo"] = "Invoice INV-202611-0001",
        ["vnp_PayDate"] = "20261127103500",
        ["vnp_ResponseCode"] = responseCode,
        ["vnp_TmnCode"] = MerchantCode,
        ["vnp_TransactionNo"] = "14123456",
        ["vnp_TransactionStatus"] = transactionStatus,
        ["vnp_TxnRef"] = PaymentId.ToString("N")
    };

    private static Dictionary<string, string> Signed(Dictionary<string, string> fields, string secret = Secret)
    {
        var signed = new Dictionary<string, string>(fields)
        {
            [VnPaySignature.SecureHashField] = VnPaySignature.Sign(VnPaySignature.BuildData(fields), secret),
            [VnPaySignature.SecureHashTypeField] = "HmacSHA512"
        };
        return signed;
    }

    private static GatewayCallback Callback(Dictionary<string, string> fields) => new(fields, Body: null);

    [Fact]
    public async Task CreateCheckout_SignsTheSortedEncodedFields()
    {
        var checkout = await _gateway.CreateCheckoutAsync(CheckoutRequest());

        Assert.Equal(
            $"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?{ExpectedData}&vnp_SecureHash={ExpectedHash}",
            checkout.PaymentUrl);
    }

    [Fact]
    public void Sign_MatchesTheKnownVector() => Assert.Equal(ExpectedHash, VnPaySignature.Sign(ExpectedData, Secret));

    [Fact]
    public async Task CreateCheckout_UrlFieldsDecodeToTheRequest()
    {
        var checkout = await _gateway.CreateCheckoutAsync(CheckoutRequest());

        var query = QueryHelpers.ParseQuery(new Uri(checkout.PaymentUrl).Query);
        Assert.Equal("300000000", query["vnp_Amount"]);
        Assert.Equal("Invoice INV-202611-0001", query["vnp_OrderInfo"]);
        Assert.Equal("20261127103000", query["vnp_CreateDate"]);
        Assert.Equal("20261127104500", query["vnp_ExpireDate"]);
        Assert.Equal("6f1c0b9a2d4e4f7a9b3c1d2e3f405162", query["vnp_TxnRef"]);
        Assert.Equal($"{ReturnUrl}?invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f", query["vnp_ReturnUrl"]);
        Assert.True(VnPaySignature.Verify(query.ToDictionary(q => q.Key, q => q.Value.ToString()), Secret));
    }

    [Theory]
    [InlineData("https://app.test/return", "https://app.test/return?invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f")]
    [InlineData("https://app.test/return?lang=vi", "https://app.test/return?lang=vi&invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f")]
    [InlineData("https://app.test/return?", "https://app.test/return?invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f")]
    [InlineData("https://app.test/return?lang=vi&", "https://app.test/return?lang=vi&invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f")]
    public void ReturnUrlOf_AddsTheInvoiceIdAsAQueryParameter(string returnUrl, string expected) =>
        Assert.Equal(expected, VnPayGateway.ReturnUrlOf(returnUrl, InvoiceId));

    [Fact]
    public async Task CreateCheckout_ReturnUrlWithAQuery_IsEncodedIntoOneSignedField()
    {
        var checkout = await Gateway("https://app.test/return?lang=vi").CreateCheckoutAsync(CheckoutRequest());

        // The ? and & of the return URL are encoded: they do not split the payment URL's own query string
        var query = QueryHelpers.ParseQuery(new Uri(checkout.PaymentUrl).Query);
        Assert.Equal("https://app.test/return?lang=vi&invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f", query["vnp_ReturnUrl"]);
        Assert.False(query.ContainsKey("invoiceId"));
        Assert.False(query.ContainsKey("lang"));
        Assert.True(VnPaySignature.Verify(query.ToDictionary(q => q.Key, q => q.Value.ToString()), Secret));
    }

    [Fact]
    public async Task CreateCheckout_PayloadKeepsTheReturnUrlWithTheInvoiceId()
    {
        var checkout = await _gateway.CreateCheckoutAsync(CheckoutRequest());

        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(checkout.RequestPayload)!;
        Assert.Equal($"{ReturnUrl}?invoiceId=3c9d2e7f-1a4b-4c5d-8e6f-7a8b9c0d1e2f", payload["vnp_ReturnUrl"]);
    }

    [Fact]
    public void VerifyCallback_ReturnWithTheInvoiceIdParameter_IgnoresIt()
    {
        // VNPay appends its vnp_* fields to our return URL; invoiceId is not part of what it signed
        var fields = Signed(IpnFields());
        fields[VnPayGateway.InvoiceIdParameter] = InvoiceId.ToString();

        Assert.True(_gateway.VerifyCallback(Callback(fields), Credentials));
    }

    [Fact]
    public async Task CreateCheckout_PayloadHasTheFieldsButNoSecretOrHash()
    {
        var checkout = await _gateway.CreateCheckoutAsync(CheckoutRequest());

        Assert.DoesNotContain(Secret, checkout.RequestPayload);
        Assert.DoesNotContain(ExpectedHash, checkout.RequestPayload);
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(checkout.RequestPayload)!;
        Assert.Equal("TESTCODE", payload["vnp_TmnCode"]);
        Assert.Null(checkout.ResponsePayload);
    }

    [Theory]
    [InlineData(3_000_000, "300000000")]
    [InlineData(1, "100")]
    [InlineData(1_234_567, "123456700")]
    public void ToVnPayAmount_MultipliesBy100(decimal amount, string expected) =>
        Assert.Equal(expected, VnPayGateway.ToVnPayAmount(amount));

    [Fact]
    public void FormatDate_UsesVietnamTime() =>
        Assert.Equal("20261128064500", VnPayGateway.FormatDate(new DateTimeOffset(2026, 11, 27, 23, 45, 0, TimeSpan.Zero)));

    [Fact]
    public void BuildData_SkipsHashFieldsEmptyValuesAndOtherParameters()
    {
        var data = VnPaySignature.BuildData(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "abc",
            ["vnp_BankTranNo"] = "",
            ["vnp_SecureHash"] = "x",
            ["vnp_SecureHashType"] = "HmacSHA512",
            ["utm_source"] = "mail",
            ["vnp_Amount"] = "100"
        });

        Assert.Equal("vnp_Amount=100&vnp_TxnRef=abc", data);
    }

    [Fact]
    public void ParseCallback_Success_ReadsPaymentAmountAndTransaction()
    {
        var data = _gateway.ParseCallback(Callback(Signed(IpnFields())));

        Assert.Equal(PaymentId, data.PaymentId);
        Assert.Equal(3_000_000m, data.Amount);
        Assert.True(data.IsSuccess);
        Assert.Equal("14123456", data.GatewayTransactionId);
    }

    [Theory]
    [InlineData("24", "02")] // the tenant cancelled
    [InlineData("00", "02")] // paid but the transaction failed at the bank
    [InlineData("11", "00")]
    public void ParseCallback_NotBothCodesSuccess_IsAFailureWithoutTransactionId(string responseCode, string transactionStatus)
    {
        var data = _gateway.ParseCallback(Callback(Signed(IpnFields(responseCode, transactionStatus))));

        Assert.False(data.IsSuccess);
        Assert.Null(data.GatewayTransactionId);
        Assert.Equal(PaymentId, data.PaymentId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("6f1c0b9a-2d4e-4f7a-9b3c-1d2e3f405162")] // "D" format: we always send "N"
    public void ParseCallback_UnreadableTxnRef_HasNoPayment(string txnRef)
    {
        var fields = IpnFields();
        fields["vnp_TxnRef"] = txnRef;

        Assert.Null(_gateway.ParseCallback(Callback(fields)).PaymentId);
    }

    [Fact]
    public void ParseCallback_UnreadableAmount_HasNoAmount() =>
        Assert.Null(_gateway.ParseCallback(Callback(IpnFields(amount: "-5"))).Amount);

    [Fact]
    public void ParseCallback_MasksTheHashInThePayload()
    {
        var fields = Signed(IpnFields());

        var data = _gateway.ParseCallback(Callback(fields));

        Assert.DoesNotContain(fields[VnPaySignature.SecureHashField], data.Payload);
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(data.Payload)!;
        Assert.Equal("***", payload[VnPaySignature.SecureHashField]);
        Assert.Equal("14123456", payload["vnp_TransactionNo"]);
    }

    [Fact]
    public void VerifyCallback_SignedWithTheSecret_IsTrue() =>
        Assert.True(_gateway.VerifyCallback(Callback(Signed(IpnFields())), Credentials));

    [Fact]
    public void VerifyCallback_UppercaseHash_IsTrue()
    {
        var fields = Signed(IpnFields());
        fields[VnPaySignature.SecureHashField] = fields[VnPaySignature.SecureHashField].ToUpperInvariant();

        Assert.True(_gateway.VerifyCallback(Callback(fields), Credentials));
    }

    [Fact]
    public void VerifyCallback_TamperedAmount_IsFalse()
    {
        var fields = Signed(IpnFields());
        fields["vnp_Amount"] = "100";

        Assert.False(_gateway.VerifyCallback(Callback(fields), Credentials));
    }

    [Fact]
    public void VerifyCallback_OtherSecret_IsFalse() =>
        Assert.False(_gateway.VerifyCallback(Callback(Signed(IpnFields(), "OTHERSECRET")), Credentials));

    [Fact]
    public void VerifyCallback_OtherMerchant_IsFalse() =>
        Assert.False(_gateway.VerifyCallback(Callback(Signed(IpnFields())), Credentials with { MerchantCode = "OTHER" }));

    [Fact]
    public void VerifyCallback_WithoutHash_IsFalse() =>
        Assert.False(_gateway.VerifyCallback(Callback(IpnFields()), Credentials));

    [Theory]
    [InlineData(GatewayCallbackOutcome.Received, "00")]
    [InlineData(GatewayCallbackOutcome.AlreadyProcessed, "02")]
    [InlineData(GatewayCallbackOutcome.PaymentNotFound, "01")]
    [InlineData(GatewayCallbackOutcome.InvalidAmount, "04")]
    [InlineData(GatewayCallbackOutcome.InvalidSignature, "97")]
    [InlineData(GatewayCallbackOutcome.Error, "99")]
    public void BuildCallbackResponse_ReturnsTheVnPayCode(GatewayCallbackOutcome outcome, string expectedCode)
    {
        var response = _gateway.BuildCallbackResponse(outcome);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        using var json = JsonDocument.Parse(response.Body!);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("RspCode").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("Message").GetString()));
    }

    [Fact]
    public void Gateway_IsVnPay() => Assert.Equal(PaymentGateway.VnPay, _gateway.Gateway);

    [Fact]
    public void ToVnPayAmount_RoundsFractionalDong() => Assert.Equal("100", VnPayGateway.ToVnPayAmount(0.999m));

    [Fact]
    public void ParseCallback_TransactionNoTooLong_HasNoTransactionId()
    {
        var fields = IpnFields();
        fields["vnp_TransactionNo"] = new string('1', PaymentTransaction.GatewayTransactionIdMaxLength + 1);

        var data = _gateway.ParseCallback(Callback(fields));

        Assert.True(data.IsSuccess);
        Assert.Null(data.GatewayTransactionId);
    }

    [Fact]
    public void ParseCallback_EmptyQuery_DoesNotThrow()
    {
        var data = _gateway.ParseCallback(Callback([]));

        Assert.Null(data.PaymentId);
        Assert.Null(data.Amount);
        Assert.False(data.IsSuccess);
        Assert.Null(data.GatewayTransactionId);
    }

    [Fact]
    public void VnPayOptions_WithoutUrls_IsInvalid()
    {
        var options = new VnPayOptions();
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(VnPayOptions.PaymentUrl)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(VnPayOptions.ReturnUrl)));
    }
}
