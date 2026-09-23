using BoardingHouse.Api.DTOs.Rooms;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateRoomAmenityRequestValidatorTests
{
    private readonly CreateRoomAmenityRequestValidator _validator = new();

    private static CreateRoomAmenityRequest ValidRequest() => new() { Name = "WiFi" };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyName_HasError(string name)
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

    [Fact]
    public void Validate_NameExactly100Characters_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = new string('a', 100) });

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_QuantityLessThanOne_HasError(int quantity)
    {
        var result = _validator.TestValidate(ValidRequest() with { Quantity = quantity });

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Fact]
    public void Validate_QuantityOmitted_IsNullAndHasNoErrors()
    {
        var request = ValidRequest();

        var result = _validator.TestValidate(request);

        Assert.Null(request.Quantity);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("wifi")]
    [InlineData("air-conditioner")]
    [InlineData("tv2")]
    public void Validate_KebabCaseIcon_HasNoError(string icon)
    {
        var result = _validator.TestValidate(ValidRequest() with { Icon = icon });

        result.ShouldNotHaveValidationErrorFor(x => x.Icon);
    }

    [Theory]
    [InlineData("")]
    [InlineData("WiFi")]
    [InlineData("air_conditioner")]
    [InlineData("air conditioner")]
    [InlineData("-wifi")]
    [InlineData("wifi-")]
    [InlineData("air--conditioner")]
    public void Validate_NonKebabCaseIcon_HasError(string icon)
    {
        var result = _validator.TestValidate(ValidRequest() with { Icon = icon });

        result.ShouldHaveValidationErrorFor(x => x.Icon);
    }

    [Fact]
    public void Validate_IconExceeds50Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Icon = new string('a', 51) });

        result.ShouldHaveValidationErrorFor(x => x.Icon);
    }
}
