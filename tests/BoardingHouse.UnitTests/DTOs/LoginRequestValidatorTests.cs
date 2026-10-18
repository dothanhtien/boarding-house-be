using BoardingHouse.Api.DTOs.Auth;

namespace BoardingHouse.UnitTests.DTOs;

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    private static LoginRequest Valid() => new() { Identifier = "user@test.com", Password = "password123" };

    [Theory]
    [InlineData("user@test.com")]
    [InlineData("0912345678")]
    [InlineData("+84 912.345.678")]
    public void Validate_EmailOrPhone_IsValid(string identifier)
    {
        var result = _validator.Validate(Valid() with { Identifier = identifier });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("@test.com")]
    [InlineData("not-an-email")]
    [InlineData("09abc")]
    public void Validate_InvalidIdentifier_HasIdentifierError(string identifier)
    {
        var result = _validator.Validate(Valid() with { Identifier = identifier });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginRequest.Identifier));
    }

    [Fact]
    public void Validate_EmptyPassword_HasPasswordError()
    {
        var result = _validator.Validate(Valid() with { Password = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginRequest.Password));
    }
}
