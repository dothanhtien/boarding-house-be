using BoardingHouse.Api.DTOs.Rooms;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateRoomAmenityRequestValidatorTests
{
    private readonly UpdateRoomAmenityRequestValidator _validator = new();

    [Fact]
    public void Validate_NewAmenityWithName_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Name = "WiFi" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NewAmenityWithoutName_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Quantity = 2 });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_IsDeletedWithoutId_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Name = "WiFi", IsDeleted = true });

        result.ShouldHaveValidationErrorFor(x => x.IsDeleted);
    }

    [Fact]
    public void Validate_DeleteExistingAmenityWithIdOnly_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), IsDeleted = true });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_UpdateExistingAmenityQuantityOnly_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), Quantity = 3 });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ExistingAmenityWithBlankName_HasError(string name)
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), Name = name });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_ExistingAmenityQuantitySetToNull_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), Quantity = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_QuantityLessThanOne_HasError(int quantity)
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Name = "WiFi", Quantity = quantity });

        result.ShouldHaveValidationErrorFor("Quantity");
    }

    [Fact]
    public void Validate_ExistingAmenityIconSetToNull_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), Icon = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_KebabCaseIcon_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), Icon = "air-conditioner" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Air_Conditioner")]
    public void Validate_NonKebabCaseIcon_HasError(string icon)
    {
        var result = _validator.TestValidate(new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), Icon = icon });

        result.ShouldHaveValidationErrorFor("Icon");
    }
}
