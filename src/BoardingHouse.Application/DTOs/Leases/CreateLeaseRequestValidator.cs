using FluentValidation;
using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Leases;

public class CreateLeaseRequestValidator : AbstractValidator<CreateLeaseRequest>
{
    public CreateLeaseRequestValidator()
    {
        RuleFor(x => x.RoomId).NotEmpty().WithMessage("RoomId is required");

        RuleFor(x => x.LeaseCode)
            .NotEmpty().WithMessage("LeaseCode must not be empty")
            .MaximumLength(50).WithMessage("LeaseCode must not exceed 50 characters")
            .When(x => x.LeaseCode is not null);

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage("EndDate must be after StartDate")
            .When(x => x.EndDate is not null);

        RuleFor(x => x.BillingDay)
            .InclusiveBetween(1, 28).WithMessage("BillingDay must be between 1 and 28")
            .When(x => x.BillingDay is not null);

        RuleFor(x => x.MonthlyRent)
            .GreaterThan(0).WithMessage("MonthlyRent must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("MonthlyRent must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.MonthlyRent is not null);

        RuleFor(x => x.DepositAmount)
            .GreaterThanOrEqualTo(0).WithMessage("DepositAmount must not be negative")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DepositAmount must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DepositAmount is not null);

        RuleFor(x => x.Tenants!)
            .Must(t => t.Count <= Lease.MaxTenants)
            .WithMessage($"A lease must not have more than {Lease.MaxTenants} tenants")
            .Must(t => t.Where(x => x is not null).Select(x => x.UserId).AllUnique())
            .WithMessage("Each user can only appear once in a lease")
            .Must(t => t.Count(x => x is { IsRepresentative: true }) <= 1)
            .WithMessage("A lease can have at most one representative tenant")
            .When(x => x.Tenants is not null);

        RuleForEach(x => x.Tenants)
            .NotNull().WithMessage("Tenant must not be null")
            .SetValidator(new CreateLeaseTenantRequestValidator());

        RuleFor(x => x.UtilityServices!)
            .Must(s => s.Where(x => x is not null).Select(x => x.ServiceId).AllUnique())
            .WithMessage("Each utility service can only appear once in a lease")
            .When(x => x.UtilityServices is not null);

        RuleForEach(x => x.UtilityServices)
            .NotNull().WithMessage("Utility service must not be null")
            .SetValidator(new CreateLeaseUtilityServiceRequestValidator());
    }
}
