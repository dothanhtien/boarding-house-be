using FluentValidation;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Leases;

public class UpdateLeaseRequestValidator : AbstractValidator<UpdateLeaseRequest>
{
    public UpdateLeaseRequestValidator()
    {
        RuleFor(x => x.LeaseCode.Value)
            .NotEmpty().WithMessage("LeaseCode is required")
            .MaximumLength(50).WithMessage("LeaseCode must not exceed 50 characters")
            .When(x => x.LeaseCode.IsSet)
            .OverridePropertyName("LeaseCode");

        RuleFor(x => x.BillingDay.Value)
            .InclusiveBetween(1, 28).WithMessage("BillingDay must be between 1 and 28")
            .When(x => x.BillingDay is { IsSet: true, Value: not null })
            .OverridePropertyName("BillingDay");

        RuleFor(x => x.MonthlyRent.Value)
            .GreaterThan(0).WithMessage("MonthlyRent must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("MonthlyRent must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.MonthlyRent.IsSet)
            .OverridePropertyName("MonthlyRent");

        RuleFor(x => x.DepositAmount.Value)
            .GreaterThanOrEqualTo(0).WithMessage("DepositAmount must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DepositAmount must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DepositAmount.IsSet)
            .OverridePropertyName("DepositAmount");

        RuleFor(x => x.Tenants.Value)
            .NotNull().WithMessage("Tenants must not be null — omit the field to leave tenants unchanged")
            .Must(t => t is null || t.Count <= Lease.MaxTenants)
            .WithMessage($"Tenants must not contain more than {Lease.MaxTenants} changes per request")
            .Must(t => t is null || t.Where(x => x?.Id is not null).Select(x => x!.Id).AllUnique())
            .WithMessage("Each tenant Id must appear at most once")
            .Must(t => t is null || t.Where(x => x is { Id: null }).Select(x => x!.UserId).AllUnique())
            .WithMessage("Each user can only be added once")
            .When(x => x.Tenants.IsSet)
            .OverridePropertyName("Tenants");

        RuleForEach(x => x.Tenants.Value)
            .NotNull().WithMessage("Tenant must not be null")
            .SetValidator(new UpdateLeaseTenantRequestValidator())
            .When(x => x.Tenants is { IsSet: true, Value: not null })
            .OverridePropertyName("Tenants");
    }
}
