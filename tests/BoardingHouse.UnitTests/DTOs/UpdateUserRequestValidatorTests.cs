using BoardingHouse.Api.DTOs.Users;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateUserRequestValidatorTests
{
    private readonly UpdateUserRequestValidator _validator = new();

    private static UpdateUserRequest Valid() => new()
    {
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
    public void Validate_NullPhone_IsValid()
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
}
