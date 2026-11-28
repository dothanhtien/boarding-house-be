using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.PaymentGateways;

namespace BoardingHouse.UnitTests.DTOs;

public class OnlinePaymentReturnRequestValidatorTests
{
    private readonly OnlinePaymentReturnRequestValidator _validator = new();

    private static OnlinePaymentReturnRequest With(Dictionary<string, string>? @params) => new() { Params = @params };

    [Fact]
    public void ReturnParameters_Pass() =>
        _validator.TestValidate(With(new() { ["vnp_TxnRef"] = "abc", ["invoiceId"] = Guid.NewGuid().ToString() }))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void WithoutParams_Fails() =>
        _validator.TestValidate(With(null)).ShouldHaveValidationErrorFor(x => x.Params).WithErrorMessage("Params is required");

    [Fact]
    public void EmptyParams_Fails() =>
        _validator.TestValidate(With([])).ShouldHaveValidationErrorFor(x => x.Params)
            .WithErrorMessage("Params must not be empty");

    [Fact]
    public void TooManyParams_Fails() =>
        _validator.TestValidate(With(Enumerable.Range(0, OnlinePaymentReturnRequestValidator.MaxParams + 1)
                .ToDictionary(i => $"p{i}", _ => "x")))
            .ShouldHaveValidationErrorFor(x => x.Params)
            .WithErrorMessage("Params must not have more than 50 entries");

    [Fact]
    public void TooLongName_Fails() =>
        _validator.TestValidate(With(new() { [new string('k', OnlinePaymentReturnRequestValidator.MaxKeyLength + 1)] = "x" }))
            .ShouldHaveValidationErrorFor(x => x.Params)
            .WithErrorMessage("Each param name must be 1 to 100 characters");

    [Fact]
    public void TooLongValue_Fails() =>
        _validator.TestValidate(With(new() { ["vnp_OrderInfo"] = new string('v', OnlinePaymentReturnRequestValidator.MaxValueLength + 1) }))
            .ShouldHaveValidationErrorFor(x => x.Params)
            .WithErrorMessage("Each param value must be a string of at most 1000 characters");
}
