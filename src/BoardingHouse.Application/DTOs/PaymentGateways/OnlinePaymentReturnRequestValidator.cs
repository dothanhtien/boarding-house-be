using FluentValidation;

namespace BoardingHouse.Application.DTOs.PaymentGateways;

public class OnlinePaymentReturnRequestValidator : AbstractValidator<OnlinePaymentReturnRequest>
{
    public const int MaxParams = 50;
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 1000;

    public OnlinePaymentReturnRequestValidator()
    {
        RuleFor(x => x.Params)
            .NotNull().WithMessage("Params is required")
            .NotEmpty().WithMessage("Params must not be empty")
            .Must(p => p!.Count <= MaxParams).WithMessage($"Params must not have more than {MaxParams} entries")
            .Must(p => p!.Keys.All(k => k.Length is > 0 and <= MaxKeyLength))
                .WithMessage($"Each param name must be 1 to {MaxKeyLength} characters")
            .Must(p => p!.Values.All(v => v is not null && v.Length <= MaxValueLength))
                .WithMessage($"Each param value must be a string of at most {MaxValueLength} characters");
    }
}
