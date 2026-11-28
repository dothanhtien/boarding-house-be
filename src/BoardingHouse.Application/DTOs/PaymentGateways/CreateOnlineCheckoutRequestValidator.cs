using FluentValidation;

namespace BoardingHouse.Application.DTOs.PaymentGateways;

public class CreateOnlineCheckoutRequestValidator : AbstractValidator<CreateOnlineCheckoutRequest>
{
    public CreateOnlineCheckoutRequestValidator()
    {
        RuleFor(x => x.Gateway).NotNull().WithMessage("Gateway is required");
    }
}
