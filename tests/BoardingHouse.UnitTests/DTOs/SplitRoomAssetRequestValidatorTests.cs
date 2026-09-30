using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class SplitRoomAssetRequestValidatorTests
{
    private readonly SplitRoomAssetRequestValidator _validator = new();

    private static SplitRoomAssetRequest ValidRequest() => new() { Quantity = 1, NewCondition = AssetCondition.Damaged };

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_QuantityBelowOne_HasError(int quantity)
    {
        var result = _validator.TestValidate(ValidRequest() with { Quantity = quantity });

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Validate_UndefinedCondition_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { NewCondition = (AssetCondition)99 });

        result.ShouldHaveValidationErrorFor(x => x.NewCondition);
    }
}
