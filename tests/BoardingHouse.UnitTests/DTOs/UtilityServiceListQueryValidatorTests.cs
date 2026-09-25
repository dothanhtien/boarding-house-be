using BoardingHouse.Api.DTOs.UtilityServices;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UtilityServiceListQueryValidatorTests
{
    private readonly UtilityServiceListQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyPropertyId_HasError()
    {
        var result = _validator.TestValidate(new UtilityServiceListQuery());

        result.ShouldHaveValidationErrorFor(x => x.PropertyId);
    }

    [Fact]
    public void Validate_PropertyIdSet_HasNoErrors()
    {
        var result = _validator.TestValidate(new UtilityServiceListQuery { PropertyId = Guid.NewGuid() });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
