using FluentValidation;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.DTOs.PaymentGateways;

public class UpsertOrganizationPaymentGatewayRequestValidator : AbstractValidator<UpsertOrganizationPaymentGatewayRequest>
{
    public const int SecretMaxLength = 256;

    public UpsertOrganizationPaymentGatewayRequestValidator()
    {
        RuleFor(x => x.MerchantCode)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("MerchantCode is required")
            .Must(code => code.Trim().Length <= OrganizationPaymentGateway.MerchantCodeMaxLength)
            .WithMessage($"MerchantCode must not exceed {OrganizationPaymentGateway.MerchantCodeMaxLength} characters");

        RuleFor(x => x.Secret)
            .Must(secret => !string.IsNullOrWhiteSpace(secret)).WithMessage("Secret must not be blank")
            .MaximumLength(SecretMaxLength).WithMessage($"Secret must not exceed {SecretMaxLength} characters")
            .When(x => x.Secret is not null);

        RuleFor(x => x.IsActive)
            .NotNull().WithMessage("IsActive is required");
    }
}
