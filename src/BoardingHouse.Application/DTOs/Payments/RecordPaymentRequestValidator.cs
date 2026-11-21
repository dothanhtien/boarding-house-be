using FluentValidation;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Payments;

public class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage(Payment.AmountPrecisionMessage);

        RuleFor(x => x.Method)
            .NotNull().WithMessage("Method is required")
            .Must(method => method is PaymentMethod.Cash or PaymentMethod.BankTransfer)
            .WithMessage("Method must be cash or bankTransfer");

        RuleFor(x => x.PaymentDate)
            .NotNull().WithMessage("PaymentDate is required")
            .Must(date => date <= DateTimeOffset.UtcNow + Payment.MaxClockSkew)
            .WithMessage("PaymentDate must not be in the future");

        RuleFor(x => x.PayerNameOverride)
            .Empty().WithMessage("Send either PayerUserId or PayerNameOverride, not both")
            .When(x => x.PayerUserId is not null);

        RuleFor(x => x.PayerNameOverride)
            .Must(name => name!.Trim().Length <= Payment.PayerNameOverrideMaxLength)
            .WithMessage($"PayerNameOverride must not exceed {Payment.PayerNameOverrideMaxLength} characters")
            .When(x => x.PayerNameOverride is not null);

        RuleFor(x => x.TransactionReference)
            .Must(reference => reference!.Trim().Length <= Payment.TransactionReferenceMaxLength)
            .WithMessage($"TransactionReference must not exceed {Payment.TransactionReferenceMaxLength} characters")
            .When(x => x.TransactionReference is not null);

        RuleFor(x => x.Note)
            .MaximumLength(Payment.NoteMaxLength)
            .WithMessage($"Note must not exceed {Payment.NoteMaxLength} characters");
    }
}
