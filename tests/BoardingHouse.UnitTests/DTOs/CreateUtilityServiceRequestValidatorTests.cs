using System.Globalization;
using BoardingHouse.Api.DTOs.UtilityServices;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateUtilityServiceRequestValidatorTests
{
    private readonly CreateUtilityServiceRequestValidator _validator = new();

    private static CreateUtilityServiceRequest ValidRequest() => new()
    {
        PropertyId = Guid.NewGuid(),
        Name = "Electricity",
        Type = UtilityType.Electricity,
        Unit = "kWh"
    };

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyPropertyId_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { PropertyId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.PropertyId);
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

    [Fact]
    public void Validate_NameAt100Characters_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = new string('a', 100) });

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_TypeOutOfRange_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Type = (UtilityType)99 });

        result.ShouldHaveValidationErrorFor(x => x.Type);
    }

    [Fact]
    public void Validate_EmptyUnit_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Unit = "" });

        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    [Fact]
    public void Validate_UnitExceeds30Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Unit = new string('a', 31) });

        result.ShouldHaveValidationErrorFor(x => x.Unit);
    }

    [Fact]
    public void Validate_UnitAt30Characters_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Unit = new string('a', 30) });

        result.ShouldNotHaveValidationErrorFor(x => x.Unit);
    }

    [Fact]
    public void Validate_DefaultUnitPriceNull_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultUnitPrice = null });

        result.ShouldNotHaveValidationErrorFor(x => x.DefaultUnitPrice);
    }

    [Fact]
    public void Validate_DefaultUnitPriceZero_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultUnitPrice = 0 });

        result.ShouldNotHaveValidationErrorFor(x => x.DefaultUnitPrice);
    }

    [Fact]
    public void Validate_DefaultUnitPriceNegative_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultUnitPrice = -1 });

        result.ShouldHaveValidationErrorFor(x => x.DefaultUnitPrice);
    }

    [Theory]
    [InlineData("3500.555")]
    [InlineData("10000000000000000")]
    public void Validate_DefaultUnitPriceExceedsPrecisionOrScale_HasError(string price)
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultUnitPrice = decimal.Parse(price, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor(x => x.DefaultUnitPrice);
    }

    [Fact]
    public void Validate_DefaultUnitPriceAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultUnitPrice = 9999999999999999.99m });

        result.ShouldNotHaveValidationErrorFor(x => x.DefaultUnitPrice);
    }
}
