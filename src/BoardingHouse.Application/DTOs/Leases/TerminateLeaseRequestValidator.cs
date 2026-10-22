using FluentValidation;

namespace BoardingHouse.Application.DTOs.Leases;

public class TerminateLeaseRequestValidator : AbstractValidator<TerminateLeaseRequest>
{
    public TerminateLeaseRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required")
            .MaximumLength(1000).WithMessage("Reason must not exceed 1000 characters");
    }
}
