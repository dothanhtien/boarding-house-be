using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateRoomAssetConditionHistoryRequestValidatorTests
{
    private readonly CreateRoomAssetConditionHistoryRequestValidator _validator = new();

    [Theory]
    [InlineData(AssetCondition.Good)]
    [InlineData(AssetCondition.Damaged)]
    [InlineData(AssetCondition.Broken)]
    public void Validate_KnownCondition_HasNoErrors(AssetCondition condition)
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest { NewCondition = condition });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_UndefinedCondition_HasError()
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest { NewCondition = (AssetCondition)99 });

        result.ShouldHaveValidationErrorFor(x => x.NewCondition);
    }
}
