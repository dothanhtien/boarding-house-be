using BoardingHouse.Api.DTOs.Auth;

namespace BoardingHouse.UnitTests.DTOs;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Valid() => new()
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

    [Fact]
    public void Validate_NullPhone_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = null });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_InvalidEmail_HasEmailError(string email)
    {
        var result = _validator.Validate(Valid() with { Email = email });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Email));
    }

    [Fact]
    public void Validate_EmailExceeds255Characters_HasEmailError()
    {
        var localPart = new string('a', 250);
        var result = _validator.Validate(Valid() with { Email = $"{localPart}@test.com" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Email));
    }

    [Fact]
    public void Validate_PhoneExceeds20Characters_HasPhoneError()
    {
        var result = _validator.Validate(Valid() with { Phone = new string('1', 21) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Phone));
    }

    [Fact]
    public void Validate_EmptyFullName_HasFullNameError()
    {
        var result = _validator.Validate(Valid() with { FullName = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.FullName));
    }

    [Fact]
    public void Validate_FullNameExceeds255Characters_HasFullNameError()
    {
        var result = _validator.Validate(Valid() with { FullName = new string('a', 256) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.FullName));
    }

    [Fact]
    public void Validate_PasswordShorterThan8Characters_HasPasswordError()
    {
        var result = _validator.Validate(Valid() with { Password = "short1", PasswordConfirmation = "short1" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Validate_PasswordExceeds72Characters_HasPasswordError()
    {
        var password = new string('a', 73);
        var result = _validator.Validate(Valid() with { Password = password, PasswordConfirmation = password });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Validate_PasswordConfirmationMismatch_HasPasswordConfirmationError()
    {
        var result = _validator.Validate(Valid() with { PasswordConfirmation = "different-password" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.PasswordConfirmation));
    }
}
