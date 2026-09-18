using FluentValidation;

namespace BoardingHouse.Api.DTOs.Organizations
{
    public class AddOrganizationMemberRequestValidator : AbstractValidator<AddOrganizationMemberRequest>
    {
        public AddOrganizationMemberRequestValidator()
        {
            RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required");
            RuleFor(x => x.RoleId).NotEmpty().WithMessage("RoleId is required");
        }
    }
}