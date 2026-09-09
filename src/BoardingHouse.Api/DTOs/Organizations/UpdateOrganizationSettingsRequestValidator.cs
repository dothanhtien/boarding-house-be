using FluentValidation;

namespace BoardingHouse.Api.DTOs.Organizations;

public class UpdateOrganizationSettingsRequestValidator : AbstractValidator<UpdateOrganizationSettingsRequest>
{
    public UpdateOrganizationSettingsRequestValidator()
    {
        RuleFor(x => x.DefaultBillingDay)
            .InclusiveBetween(1, 28).WithMessage("Default billing day must be between 1 and 28")
            .When(x => x.DefaultBillingDay is not null);

        RuleFor(x => x)
            .Must(x => (x.LateFeeType is null) == (x.LateFeeValue is null))
            .WithMessage("Late fee type and Late fee value must be set together")
            .WithName(nameof(UpdateOrganizationSettingsRequest.LateFeeValue));

        RuleFor(x => x.LateFeeType)
            .IsInEnum().WithMessage("Late fee type is invalid")
            .When(x => x.LateFeeType is not null);

        RuleFor(x => x.LateFeeGraceDays)
            .GreaterThanOrEqualTo(0).WithMessage("Late fee grace days must not be negative")
            .When(x => x.LateFeeGraceDays is not null);

        RuleFor(x => x.VatRate)
            .InclusiveBetween(0, 100).WithMessage("Vat rate must be between 0 and 100")
            .When(x => x.VatRate is not null);

        RuleFor(x => x.BankAccountNumber)
            .MaximumLength(50).WithMessage("Bank account number exceeds 50 characters")
            .When(x => x.BankAccountNumber is not null);

        RuleFor(x => x.BankName)
            .MaximumLength(100).WithMessage("Bank name exceeds 100 characters")
            .When(x => x.BankName is not null);

        RuleFor(x => x.BankAccountName)
            .MaximumLength(100).WithMessage("Bank account name exceeds 100 characters")
            .When(x => x.BankAccountName is not null);
    }
}
