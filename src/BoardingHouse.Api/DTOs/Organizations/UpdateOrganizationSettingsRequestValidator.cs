using FluentValidation;

namespace BoardingHouse.Api.DTOs.Organizations;

public class UpdateOrganizationSettingsRequestValidator : AbstractValidator<UpdateOrganizationSettingsRequest>
{
    public UpdateOrganizationSettingsRequestValidator()
    {
        RuleFor(x => x.DefaultBillingDay.Value)
            .InclusiveBetween(1, 28).WithMessage("Default billing day must be between 1 and 28")
            .When(x => x.DefaultBillingDay is { IsSet: true, Value: not null })
            .OverridePropertyName("DefaultBillingDay");

        RuleFor(x => x)
            .Must(x =>
            {
                if (!x.LateFeeType.IsSet && !x.LateFeeValue.IsSet)
                {
                    return true;
                }

                return x.LateFeeType.IsSet && x.LateFeeValue.IsSet
                    && (x.LateFeeType.Value is null) == (x.LateFeeValue.Value is null);
            })
            .WithMessage("Late fee type and Late fee value must be set together")
            .WithName(nameof(UpdateOrganizationSettingsRequest.LateFeeValue));

        RuleFor(x => x.LateFeeType.Value)
            .IsInEnum().WithMessage("Late fee type is invalid")
            .When(x => x.LateFeeType is { IsSet: true, Value: not null })
            .OverridePropertyName("LateFeeType");

        RuleFor(x => x.LateFeeGraceDays.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Late fee grace days must not be negative")
            .When(x => x.LateFeeGraceDays is { IsSet: true, Value: not null })
            .OverridePropertyName("LateFeeGraceDays");

        RuleFor(x => x.VatRate.Value)
            .InclusiveBetween(0, 100).WithMessage("Vat rate must be between 0 and 100")
            .When(x => x.VatRate is { IsSet: true, Value: not null })
            .OverridePropertyName("VatRate");

        RuleFor(x => x.BankAccountNumber.Value)
            .MaximumLength(50).WithMessage("Bank account number exceeds 50 characters")
            .When(x => x.BankAccountNumber is { IsSet: true, Value: not null })
            .OverridePropertyName("BankAccountNumber");

        RuleFor(x => x.BankName.Value)
            .MaximumLength(100).WithMessage("Bank name exceeds 100 characters")
            .When(x => x.BankName is { IsSet: true, Value: not null })
            .OverridePropertyName("BankName");

        RuleFor(x => x.BankAccountName.Value)
            .MaximumLength(100).WithMessage("Bank account name exceeds 100 characters")
            .When(x => x.BankAccountName is { IsSet: true, Value: not null })
            .OverridePropertyName("BankAccountName");
    }
}
