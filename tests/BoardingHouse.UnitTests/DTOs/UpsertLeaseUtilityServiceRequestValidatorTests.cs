using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Leases;

namespace BoardingHouse.UnitTests.DTOs;

public class UpsertLeaseUtilityServiceRequestValidatorTests
{
    private readonly UpsertLeaseUtilityServiceRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullOverride_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ZeroOverride_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = 0m });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NegativeOverride_HasError()
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = -1m });

        result.ShouldHaveValidationErrorFor("UnitPriceOverride");
    }

    [Fact]
    public void Validate_OverrideWithMoreThanTwoDecimals_HasError()
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = 1.234m });

        result.ShouldHaveValidationErrorFor("UnitPriceOverride");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveQuantity_HasError(int quantity)
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest { Quantity = quantity });

        result.ShouldHaveValidationErrorFor("Quantity");
    }

    [Fact]
    public void Validate_PositiveQuantity_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpsertLeaseUtilityServiceRequest { Quantity = 3 });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
