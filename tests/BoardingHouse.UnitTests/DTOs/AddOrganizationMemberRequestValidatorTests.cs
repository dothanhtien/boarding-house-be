using BoardingHouse.Api.DTOs.Organizations;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class AddOrganizationMemberRequestValidatorTests
{
    private readonly AddOrganizationMemberRequestValidator _validator = new();

    private static AddOrganizationMemberRequest ValidRequest() => new()
    {
        UserId = Guid.NewGuid(),
        RoleId = Guid.NewGuid()
    };

    [Fact]
    public void Validate_EmptyUserId_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { UserId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Validate_EmptyRoleId_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { RoleId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.RoleId);
    }

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }
}
