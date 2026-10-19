using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.Leases;

namespace BoardingHouse.UnitTests.DTOs;

public class TerminateLeaseRequestValidatorTests
{
    private readonly TerminateLeaseRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        var result = _validator.TestValidate(new TerminateLeaseRequest { Reason = "Moved out" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReasonEmpty_HasError(string reason)
    {
        var result = _validator.TestValidate(new TerminateLeaseRequest { Reason = reason });

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Validate_ReasonTooLong_HasError()
    {
        var result = _validator.TestValidate(new TerminateLeaseRequest { Reason = new string('a', 1001) });

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
