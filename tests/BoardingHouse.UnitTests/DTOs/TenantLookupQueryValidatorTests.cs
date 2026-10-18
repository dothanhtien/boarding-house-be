using BoardingHouse.Api.DTOs.Tenants;

namespace BoardingHouse.UnitTests.DTOs;

public class TenantLookupQueryValidatorTests
{
    private readonly TenantLookupQueryValidator _validator = new();

    [Theory]
    [InlineData("0912345678", null)]
    [InlineData("+84 912.345.678", null)]
    [InlineData(null, "tenant@test.com")]
    public void Validate_ExactlyOneIdentifier_IsValid(string? phone, string? email)
    {
        var result = _validator.Validate(new TenantLookupQuery { Phone = phone, Email = email });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    [InlineData("0912345678", "tenant@test.com")]
    public void Validate_NoneOrBoth_HasPhoneError(string? phone, string? email)
    {
        var result = _validator.Validate(new TenantLookupQuery { Phone = phone, Email = email });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TenantLookupQuery.Phone));
    }

    [Fact]
    public void Validate_InvalidPhone_HasPhoneError()
    {
        var result = _validator.Validate(new TenantLookupQuery { Phone = "09abc" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TenantLookupQuery.Phone));
    }

    [Fact]
    public void Validate_InvalidEmail_HasEmailError()
    {
        var result = _validator.Validate(new TenantLookupQuery { Email = "not-an-email" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TenantLookupQuery.Email));
    }
}
