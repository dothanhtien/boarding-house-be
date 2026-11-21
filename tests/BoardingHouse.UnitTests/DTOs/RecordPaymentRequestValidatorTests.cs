using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.DTOs;

public class RecordPaymentRequestValidatorTests
{
    private readonly RecordPaymentRequestValidator _validator = new();

    private static RecordPaymentRequest Valid() => new()
    {
        Amount = 1_000_000,
        Method = PaymentMethod.Cash,
        PaymentDate = DateTimeOffset.UtcNow.AddHours(-1)
    };

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EveryOptionalField_HasNoErrors()
    {
        _validator.TestValidate(Valid() with
        {
            Method = PaymentMethod.BankTransfer,
            PayerUserId = Guid.NewGuid(),
            TransactionReference = "FT2606010001",
            Note = "Paid at the office",
            Confirm = true
        }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_AmountNotPositive_HasError(int amount)
    {
        _validator.TestValidate(Valid() with { Amount = amount })
            .ShouldHaveValidationErrorFor(x => x.Amount)
            .WithErrorMessage("Amount must be greater than 0");
    }

    [Fact]
    public void Validate_AmountWithThreeDecimals_HasError()
    {
        _validator.TestValidate(Valid() with { Amount = 1_000.005m })
            .ShouldHaveValidationErrorFor(x => x.Amount)
            .WithErrorMessage(Payment.AmountPrecisionMessage);
    }

    [Fact]
    public void Validate_MissingMethod_HasError()
    {
        _validator.TestValidate(Valid() with { Method = null })
            .ShouldHaveValidationErrorFor(x => x.Method)
            .WithErrorMessage("Method is required");
    }

    [Theory]
    [InlineData(PaymentMethod.Momo)]
    [InlineData(PaymentMethod.ZaloPay)]
    [InlineData(PaymentMethod.VnPay)]
    public void Validate_GatewayMethod_HasError(PaymentMethod method)
    {
        _validator.TestValidate(Valid() with { Method = method })
            .ShouldHaveValidationErrorFor(x => x.Method)
            .WithErrorMessage("Method must be cash or bankTransfer");
    }

    [Fact]
    public void Validate_MissingPaymentDate_HasError()
    {
        _validator.TestValidate(Valid() with { PaymentDate = null })
            .ShouldHaveValidationErrorFor(x => x.PaymentDate)
            .WithErrorMessage("PaymentDate is required");
    }

    [Fact]
    public void Validate_PaymentDateInTheFuture_HasError()
    {
        _validator.TestValidate(Valid() with { PaymentDate = DateTimeOffset.UtcNow.AddHours(1) })
            .ShouldHaveValidationErrorFor(x => x.PaymentDate)
            .WithErrorMessage("PaymentDate must not be in the future");
    }

    [Fact]
    public void Validate_PayerUserAndPayerName_HasError()
    {
        _validator.TestValidate(Valid() with { PayerUserId = Guid.NewGuid(), PayerNameOverride = "Walk-in" })
            .ShouldHaveValidationErrorFor(x => x.PayerNameOverride)
            .WithErrorMessage("Send either PayerUserId or PayerNameOverride, not both");
    }

    [Fact]
    public void Validate_PayerUserWithBlankPayerName_HasNoErrors()
    {
        _validator.TestValidate(Valid() with { PayerUserId = Guid.NewGuid(), PayerNameOverride = "  " })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_TextTooLong_HasErrors()
    {
        var result = _validator.TestValidate(Valid() with
        {
            PayerNameOverride = new string('a', Payment.PayerNameOverrideMaxLength + 1),
            TransactionReference = new string('a', Payment.TransactionReferenceMaxLength + 1),
            Note = new string('a', Payment.NoteMaxLength + 1)
        });

        result.ShouldHaveValidationErrorFor(x => x.PayerNameOverride)
            .WithErrorMessage($"PayerNameOverride must not exceed {Payment.PayerNameOverrideMaxLength} characters");
        result.ShouldHaveValidationErrorFor(x => x.TransactionReference)
            .WithErrorMessage($"TransactionReference must not exceed {Payment.TransactionReferenceMaxLength} characters");
        result.ShouldHaveValidationErrorFor(x => x.Note)
            .WithErrorMessage($"Note must not exceed {Payment.NoteMaxLength} characters");
    }

    [Fact]
    public void Validate_TextAtMaxLengthAfterTrim_HasNoErrors()
    {
        _validator.TestValidate(Valid() with
        {
            PayerNameOverride = $"  {new string('a', Payment.PayerNameOverrideMaxLength)}  ",
            TransactionReference = $"  {new string('a', Payment.TransactionReferenceMaxLength)}  "
        }).ShouldNotHaveAnyValidationErrors();
    }
}
