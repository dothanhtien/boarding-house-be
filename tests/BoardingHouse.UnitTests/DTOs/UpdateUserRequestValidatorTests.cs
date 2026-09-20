using BoardingHouse.Api.DTOs.Users;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateUserRequestValidatorTests
{
    private readonly UpdateUserRequestValidator _validator = new();

    private static UpdateUserRequest Valid() => new()
    {
        Email = "test@example.com",
        Phone = "0900000000",
        FullName = "Test User",
        IsActive = true
    };

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllFieldsOmitted_IsValid()
    {
        var result = _validator.Validate(new UpdateUserRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OnlyOneFieldProvided_IsValid()
    {
        var result = _validator.Validate(new UpdateUserRequest { IsActive = false });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OmittedEmail_IsValid()
    {
        var result = _validator.Validate(Valid() with { Email = default });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ExplicitNullEmail_HasEmailError()
    {
        var result = _validator.Validate(Valid() with { Email = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Email));
    }

    [Fact]
    public void Validate_EmptyEmail_HasEmailError()
    {
        var result = _validator.Validate(Valid() with { Email = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Email));
    }

    [Fact]
    public void Validate_InvalidEmailFormat_HasEmailError()
    {
        var result = _validator.Validate(Valid() with { Email = "not-an-email" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Email));
    }

    [Fact]
    public void Validate_EmailExceeds255Characters_HasEmailError()
    {
        var result = _validator.Validate(Valid() with { Email = $"{new string('a', 250)}@a.com" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Email));
    }

    [Fact]
    public void Validate_OmittedPhone_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = default });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ExplicitNullPhone_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = null });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PhoneExceeds20Characters_HasPhoneError()
    {
        var result = _validator.Validate(Valid() with { Phone = new string('1', 21) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Phone));
    }

    [Theory]
    [InlineData("0900-000-000")]
    [InlineData("0900 000 000")]
    [InlineData("abc0900000")]
    [InlineData("+")]
    public void Validate_PhoneContainsNonDigitCharacters_HasPhoneError(string phone)
    {
        var result = _validator.Validate(Valid() with { Phone = phone });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Phone));
    }

    [Fact]
    public void Validate_EmptyStringPhone_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = "" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PhoneWithLeadingPlus_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = "+84900000000" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OmittedFullName_IsValid()
    {
        var result = _validator.Validate(Valid() with { FullName = default });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ExplicitNullFullName_HasFullNameError()
    {
        var result = _validator.Validate(Valid() with { FullName = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.FullName));
    }

    [Fact]
    public void Validate_EmptyFullName_HasFullNameError()
    {
        var result = _validator.Validate(Valid() with { FullName = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.FullName));
    }

    [Fact]
    public void Validate_FullNameExceeds255Characters_HasFullNameError()
    {
        var result = _validator.Validate(Valid() with { FullName = new string('a', 256) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.FullName));
    }

    [Fact]
    public void Validate_OmittedIsActive_IsValid()
    {
        var result = _validator.Validate(Valid() with { IsActive = default });

        Assert.True(result.IsValid);
    }
}
