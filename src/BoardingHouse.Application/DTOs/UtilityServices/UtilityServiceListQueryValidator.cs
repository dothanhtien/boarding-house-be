using FluentValidation;

namespace BoardingHouse.Application.DTOs.UtilityServices;

public class UtilityServiceListQueryValidator : AbstractValidator<UtilityServiceListQuery>
{
    public UtilityServiceListQueryValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty().WithMessage("PropertyId is required");
    }
}
