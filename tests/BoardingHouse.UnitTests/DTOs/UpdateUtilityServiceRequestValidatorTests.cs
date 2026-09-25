using BoardingHouse.Api.DTOs.UtilityServices;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateUtilityServiceRequestValidatorTests
{
    private readonly UpdateUtilityServiceRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyWhitespaceOrNullName_HasError(string? name)
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { Name = name! });

        result.ShouldHaveValidationErrorFor("Name");
    }

    [Fact]
    public void Validate_NameExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor("Name");
    }

    [Fact]
    public void Validate_NameAt100Characters_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { Name = new string('a', 100) });

        result.ShouldNotHaveValidationErrorFor("Name");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyOrNullUnit_HasError(string? unit)
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { Unit = unit! });

        result.ShouldHaveValidationErrorFor("Unit");
    }

    [Fact]
    public void Validate_UnitExceeds30Characters_HasError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { Unit = new string('a', 31) });

        result.ShouldHaveValidationErrorFor("Unit");
    }

    [Fact]
    public void Validate_UnitAt30Characters_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { Unit = new string('a', 30) });

        result.ShouldNotHaveValidationErrorFor("Unit");
    }

    [Fact]
    public void Validate_DefaultUnitPriceSetToNull_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { DefaultUnitPrice = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DefaultUnitPriceZero_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { DefaultUnitPrice = 0m });

        result.ShouldNotHaveValidationErrorFor("DefaultUnitPrice");
    }

    [Fact]
    public void Validate_DefaultUnitPriceNegative_HasError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { DefaultUnitPrice = -1m });

        result.ShouldHaveValidationErrorFor("DefaultUnitPrice");
    }

    [Fact]
    public void Validate_DefaultUnitPriceWithThreeDecimalPlaces_HasError()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { DefaultUnitPrice = 3500.555m });

        result.ShouldHaveValidationErrorFor("DefaultUnitPrice");
    }

    [Fact]
    public void Validate_OnlyIsActiveSet_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateUtilityServiceRequest { IsActive = false });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
