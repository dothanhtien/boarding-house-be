using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.PaymentGateways;

namespace BoardingHouse.UnitTests.DTOs;

public class UpsertOrganizationPaymentGatewayRequestValidatorTests
{
    private readonly UpsertOrganizationPaymentGatewayRequestValidator _validator = new();

    private static UpsertOrganizationPaymentGatewayRequest Valid() =>
        new() { MerchantCode = "TESTCODE", Secret = "TESTSECRET0123456789", IsActive = true };

    [Fact]
    public void Valid_Passes() => _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void WithoutSecret_Passes() =>
        _validator.TestValidate(Valid() with { Secret = null }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void IsActiveFalse_Passes() =>
        _validator.TestValidate(Valid() with { IsActive = false }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankMerchantCode_Fails(string merchantCode) =>
        _validator.TestValidate(Valid() with { MerchantCode = merchantCode })
            .ShouldHaveValidationErrorFor(x => x.MerchantCode)
            .WithErrorMessage("MerchantCode is required");

    [Fact]
    public void TooLongMerchantCode_Fails() =>
        _validator.TestValidate(Valid() with { MerchantCode = new string('A', 51) })
            .ShouldHaveValidationErrorFor(x => x.MerchantCode)
            .WithErrorMessage("MerchantCode must not exceed 50 characters");

    [Fact]
    public void MerchantCodeAtMaxLengthWithSurroundingSpaces_Passes() =>
        _validator.TestValidate(Valid() with { MerchantCode = $"  {new string('A', 50)}  " })
            .ShouldNotHaveValidationErrorFor(x => x.MerchantCode);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSecret_Fails(string secret) =>
        _validator.TestValidate(Valid() with { Secret = secret })
            .ShouldHaveValidationErrorFor(x => x.Secret)
            .WithErrorMessage("Secret must not be blank");

    [Fact]
    public void TooLongSecret_Fails() =>
        _validator.TestValidate(Valid() with { Secret = new string('s', 257) })
            .ShouldHaveValidationErrorFor(x => x.Secret)
            .WithErrorMessage("Secret must not exceed 256 characters");

    [Fact]
    public void SecretAtMaxLength_Passes() =>
        _validator.TestValidate(Valid() with { Secret = new string('s', 256) })
            .ShouldNotHaveValidationErrorFor(x => x.Secret);

    [Fact]
    public void MissingIsActive_Fails() =>
        _validator.TestValidate(Valid() with { IsActive = null })
            .ShouldHaveValidationErrorFor(x => x.IsActive)
            .WithErrorMessage("IsActive is required");
}
