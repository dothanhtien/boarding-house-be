using BoardingHouse.Api.DTOs.Organizations;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateOrganizationMemberRequestValidatorTests
{
    private readonly UpdateOrganizationMemberRequestValidator _validator = new();

    private static UpdateOrganizationMemberRequest ValidRequest() => new()
    {
        RoleId = Guid.NewGuid()
    };

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
