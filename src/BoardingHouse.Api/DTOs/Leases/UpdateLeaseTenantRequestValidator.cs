using FluentValidation;

namespace BoardingHouse.Api.DTOs.Leases;

public class UpdateLeaseTenantRequestValidator : AbstractValidator<UpdateLeaseTenantRequest>
{
    public UpdateLeaseTenantRequestValidator()
    {
        RuleFor(x => x.IsDeleted)
            .Equal(false).WithMessage("IsDeleted requires Id — only existing tenants can be deleted")
            .When(x => x.Id is null);

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required when adding a tenant")
            .When(x => x.Id is null);

        RuleFor(x => x.UserId)
            .Null().WithMessage("UserId cannot be changed on an existing tenant")
            .When(x => x.Id is not null);

        RuleFor(x => x.LeftDate)
            .Must(d => !d.IsSet).WithMessage("LeftDate can only be set on an existing tenant")
            .When(x => x.Id is null);
    }
}
