using FluentValidation;

namespace BoardingHouse.Application.DTOs.Payments;

public class PaymentListQueryValidator : AbstractValidator<PaymentListQuery>
{
    public PaymentListQueryValidator()
    {
        RuleFor(x => x.To)
            .Must((query, to) => to >= query.From).WithMessage("To must not be before From")
            .When(x => x.From is not null && x.To is not null);
    }
}
