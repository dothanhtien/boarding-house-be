using BoardingHouse.Api.DTOs.Auth;

namespace BoardingHouse.UnitTests.DTOs;

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    private static LoginRequest Valid() => new() { Email = "user@test.com", Password = "password123" };

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginRequest.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_HasPasswordError()
    {
        var result = _validator.Validate(Valid() with { Password = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginRequest.Password));
    }
}
