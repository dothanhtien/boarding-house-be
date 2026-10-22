using FluentValidation;

namespace BoardingHouse.Application.DTOs.Leases;

public class CreateLeaseTenantRequestValidator : AbstractValidator<CreateLeaseTenantRequest>
{
    public CreateLeaseTenantRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required");
    }
}
