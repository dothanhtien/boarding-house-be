using BoardingHouse.Api.DTOs.RoomAssets;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateRoomAssetRequestValidatorTests
{
    private readonly CreateRoomAssetRequestValidator _validator = new();

    private static CreateRoomAssetRequest ValidRequest() => new() { Name = "Wardrobe" };

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceName_HasError(string name)
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = name });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_QuantityBelowOne_HasError(int quantity)
    {
        var result = _validator.TestValidate(ValidRequest() with { Quantity = quantity });

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositivePurchaseUnitPrice_HasError(decimal price)
    {
        var result = _validator.TestValidate(ValidRequest() with { PurchaseUnitPrice = price });

        result.ShouldHaveValidationErrorFor(x => x.PurchaseUnitPrice);
    }

    [Fact]
    public void Validate_PurchaseUnitPriceWithThreeDecimals_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { PurchaseUnitPrice = 1.234m });

        result.ShouldHaveValidationErrorFor(x => x.PurchaseUnitPrice);
    }

    [Fact]
    public void Validate_NullPurchaseUnitPrice_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { PurchaseUnitPrice = null });

        result.ShouldNotHaveValidationErrorFor(x => x.PurchaseUnitPrice);
    }
}
