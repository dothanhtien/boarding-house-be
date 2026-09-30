using BoardingHouse.Api.DTOs.RoomAssets;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateRoomAssetRequestValidatorTests
{
    private readonly UpdateRoomAssetRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyWhitespaceOrNullName_HasError(string? name)
    {
        var result = _validator.TestValidate(new UpdateRoomAssetRequest { Name = name! });

        result.ShouldHaveValidationErrorFor("Name");
    }

    [Fact]
    public void Validate_NameExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetRequest { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor("Name");
    }

    [Fact]
    public void Validate_QuantityZero_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetRequest { Quantity = 0 });

        result.ShouldHaveValidationErrorFor("Quantity");
    }

    [Fact]
    public void Validate_PurchaseUnitPriceZero_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetRequest { PurchaseUnitPrice = 0m });

        result.ShouldHaveValidationErrorFor("PurchaseUnitPrice");
    }

    [Fact]
    public void Validate_NullableFieldsSetToNull_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetRequest
        {
            PurchaseDate = null,
            PurchaseUnitPrice = null,
            Note = null
        });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
