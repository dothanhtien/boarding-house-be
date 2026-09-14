using BoardingHouse.Api.DTOs.Users;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateUserRequestValidatorTests
{
    private readonly CreateUserRequestValidator _validator = new();

    private static CreateUserRequest Valid() => new()
    {
        Email = "user@test.com",
        Phone = "0900000000",
        FullName = "Test User",
        Password = "password123",
        PasswordConfirmation = "password123"
    };

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_InvalidEmail_HasEmailError(string email)
    {
        var result = _validator.Validate(Valid() with { Email = email });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.Email));
    }

    [Fact]
    public void Validate_PhoneExceeds20Characters_HasPhoneError()
    {
        var result = _validator.Validate(Valid() with { Phone = new string('1', 21) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.Phone));
    }

    [Theory]
    [InlineData("0900-000-000")]
    [InlineData("0900 000 000")]
    [InlineData("abc0900000")]
    [InlineData("09000000+0")]
    [InlineData("+")]
    [InlineData("")]
    public void Validate_PhoneContainsNonDigitCharacters_HasPhoneError(string phone)
    {
        var result = _validator.Validate(Valid() with { Phone = phone });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.Phone));
    }

    [Fact]
    public void Validate_PhoneWithLeadingPlus_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = "+84900000000" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyFullName_HasFullNameError()
    {
        var result = _validator.Validate(Valid() with { FullName = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.FullName));
    }

    [Fact]
    public void Validate_PasswordShorterThan8Characters_HasPasswordError()
    {
        var result = _validator.Validate(Valid() with { Password = "short1", PasswordConfirmation = "short1" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.Password));
    }

    [Fact]
    public void Validate_PasswordConfirmationMismatch_HasPasswordConfirmationError()
    {
        var result = _validator.Validate(Valid() with { PasswordConfirmation = "different-password" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.PasswordConfirmation));
    }
}
