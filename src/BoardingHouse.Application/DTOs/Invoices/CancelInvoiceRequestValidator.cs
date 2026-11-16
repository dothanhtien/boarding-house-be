using FluentValidation;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.Invoices;

public class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required")
            .Must(r => r.Trim().Length <= Invoice.CancelReasonMaxLength)
            .WithMessage($"Reason must not exceed {Invoice.CancelReasonMaxLength} characters");
    }
}
