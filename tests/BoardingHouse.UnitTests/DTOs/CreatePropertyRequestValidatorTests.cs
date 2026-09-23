using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class CreatePropertyRequestValidatorTests
{
    private readonly CreatePropertyRequestValidator _validator = new();

    private static CreatePropertyRequest ValidRequest() => new()
    {
        Name = "Test Property"
    };

    [Fact]
    public void Validate_EmptyName_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = "" });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_ProvinceExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Province = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Province);
    }

    [Fact]
    public void Validate_DistrictExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { District = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.District);
    }

    [Fact]
    public void Validate_WardExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Ward = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Ward);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Validate_DefaultBillingDayOutOfRange_HasError(int day)
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultBillingDay = day });

        result.ShouldHaveValidationErrorFor(x => x.DefaultBillingDay);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    public void Validate_DefaultBillingDayWithinRange_HasNoError(int day)
    {
        var result = _validator.TestValidate(ValidRequest() with { DefaultBillingDay = day });

        result.ShouldNotHaveValidationErrorFor(x => x.DefaultBillingDay);
    }

    [Fact]
    public void Validate_LateFeeTypeWithoutValue_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeType = LateFeeType.Fixed });

        result.ShouldHaveValidationErrorFor(x => x.LateFeeValue);
    }

    [Fact]
    public void Validate_LateFeeValueWithoutType_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeValue = 10 });

        result.ShouldHaveValidationErrorFor(x => x.LateFeeValue);
    }

    [Fact]
    public void Validate_LateFeeTypeAndValueTogether_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeType = LateFeeType.Fixed, LateFeeValue = 10 });

        result.ShouldNotHaveValidationErrorFor(x => x.LateFeeValue);
    }

    [Fact]
    public void Validate_LateFeeGraceDaysNegative_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LateFeeGraceDays = -1 });

        result.ShouldHaveValidationErrorFor(x => x.LateFeeGraceDays);
    }

    [Fact]
    public void Validate_NullOrganizationId_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { OrganizationId = null });

        result.ShouldNotHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }
}
