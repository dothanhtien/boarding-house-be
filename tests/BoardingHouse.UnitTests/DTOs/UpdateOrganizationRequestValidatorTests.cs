using BoardingHouse.Api.DTOs.Organizations;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateOrganizationRequestValidatorTests
{
    private readonly UpdateOrganizationRequestValidator _validator = new();

    private static UpdateOrganizationRequest ValidRequest() => new()
    {
        Name = "Test Organization",
        IsActive = true
    };

    [Fact]
    public void Validate_EmptyName_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = "" });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameExceeds255Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Name = new string('a', 256) });

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_TaxCodeExceeds20Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { TaxCode = new string('1', 21) });

        result.ShouldHaveValidationErrorFor(x => x.TaxCode);
    }

    [Fact]
    public void Validate_PhoneExceeds20Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Phone = new string('1', 21) });

        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Theory]
    [InlineData("0900-000-000")]
    [InlineData("0900 000 000")]
    [InlineData("abc0900000")]
    [InlineData("+")]
    public void Validate_PhoneContainsNonDigitCharacters_HasError(string phone)
    {
        var result = _validator.TestValidate(ValidRequest() with { Phone = phone });

        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void Validate_EmptyStringPhone_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Phone = "" });

        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void Validate_PhoneWithLeadingPlus_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Phone = "+84900000000" });

        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void Validate_InvalidEmail_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Email = "not-an-email" });

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_EmailExceeds255Characters_HasError()
    {
        var localPart = new string('a', 250);
        var result = _validator.TestValidate(ValidRequest() with { Email = $"{localPart}@test.com" });

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_ProvinceExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Province = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Province);
    }

    [Fact]
    public void Validate_DistrictExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { District = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.District);
    }

    [Fact]
    public void Validate_WardExceeds100Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Ward = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(x => x.Ward);
    }

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }
}
