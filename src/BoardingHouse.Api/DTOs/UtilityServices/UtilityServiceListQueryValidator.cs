using FluentValidation;

namespace BoardingHouse.Api.DTOs.UtilityServices;

public class UtilityServiceListQueryValidator : AbstractValidator<UtilityServiceListQuery>
{
    public UtilityServiceListQueryValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty().WithMessage("PropertyId is required");
    }
}
