using BoardingHouse.Api.Common;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.Tenants;

public class TenantLookupQueryValidator : AbstractValidator<TenantLookupQuery>
{
    public TenantLookupQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.Phone) != string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Send exactly one of phone or email")
            .OverridePropertyName(nameof(TenantLookupQuery.Phone));

        RuleFor(x => x.Phone)
            .Must(PhoneNumber.IsValid).WithMessage("Phone is invalid")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email is invalid")
            .MaximumLength(255).WithMessage("Email exceeds 255 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
