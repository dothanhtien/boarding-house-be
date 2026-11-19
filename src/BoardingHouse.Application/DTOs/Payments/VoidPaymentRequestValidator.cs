using FluentValidation;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Payments;

public class VoidPaymentRequestValidator : AbstractValidator<VoidPaymentRequest>
{
    public VoidPaymentRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required")
            .Must(r => r.Trim().Length <= Payment.VoidReasonMaxLength)
            .WithMessage($"Reason must not exceed {Payment.VoidReasonMaxLength} characters");
    }
}
