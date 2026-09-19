using FluentValidation;

namespace BoardingHouse.Api.DTOs.Organizations;

public class UpdateOrganizationMemberRequestValidator : AbstractValidator<UpdateOrganizationMemberRequest>
{
    public UpdateOrganizationMemberRequestValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("RoleId is required");
    }
}
