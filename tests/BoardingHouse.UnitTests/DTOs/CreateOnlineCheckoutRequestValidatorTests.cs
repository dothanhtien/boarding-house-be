using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateOnlineCheckoutRequestValidatorTests
{
    private readonly CreateOnlineCheckoutRequestValidator _validator = new();

    [Fact]
    public void WithGateway_Passes() =>
        _validator.TestValidate(new CreateOnlineCheckoutRequest { Gateway = PaymentGateway.VnPay })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void WithoutGateway_Fails() =>
        _validator.TestValidate(new CreateOnlineCheckoutRequest())
            .ShouldHaveValidationErrorFor(x => x.Gateway)
            .WithErrorMessage("Gateway is required");
}
