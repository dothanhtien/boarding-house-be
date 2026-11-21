using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Payments;

namespace BoardingHouse.UnitTests.DTOs;

public class PaymentListQueryValidatorTests
{
    private readonly PaymentListQueryValidator _validator = new();

    [Fact]
    public void Validate_NoDates_HasNoErrors()
    {
        _validator.TestValidate(new PaymentListQuery()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_OnlyOneDate_HasNoErrors()
    {
        _validator.TestValidate(new PaymentListQuery { From = new DateOnly(2026, 6, 1) }).ShouldNotHaveAnyValidationErrors();
        _validator.TestValidate(new PaymentListQuery { To = new DateOnly(2026, 6, 1) }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_SameDay_HasNoErrors()
    {
        _validator.TestValidate(new PaymentListQuery { From = new DateOnly(2026, 6, 1), To = new DateOnly(2026, 6, 1) })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ToBeforeFrom_HasError()
    {
        _validator.TestValidate(new PaymentListQuery { From = new DateOnly(2026, 6, 2), To = new DateOnly(2026, 6, 1) })
            .ShouldHaveValidationErrorFor(x => x.To)
            .WithErrorMessage("To must not be before From");
    }
}
