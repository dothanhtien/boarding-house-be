using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.DTOs.Tenants;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateTenantRequestValidatorTests
{
    private readonly CreateTenantRequestValidator _validator = new();

    private static CreateTenantRequest Valid() => new()
    {
        FullName = "Nguyen Van A",
        Phone = "0912345678",
        Password = "password123",
        PasswordConfirmation = "password123"
    };

    [Fact]
    public void Validate_PhoneOnly_IsValid()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_FormattedPhoneAndEmail_IsValid()
    {
        var result = _validator.Validate(Valid() with { Phone = "+84 912.345.678", Email = "tenant@test.com" });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("09abc45678")]
    public void Validate_MissingOrInvalidPhone_HasPhoneError(string phone)
    {
        var result = _validator.Validate(Valid() with { Phone = phone });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.Phone));
    }

    [Fact]
    public void Validate_InvalidEmail_HasEmailError()
    {
        var result = _validator.Validate(Valid() with { Email = "not-an-email" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.Email));
    }

    [Fact]
    public void Validate_PasswordConfirmationMismatch_HasError()
    {
        var result = _validator.Validate(Valid() with { PasswordConfirmation = "different123" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantRequest.PasswordConfirmation));
    }

    [Fact]
    public void Validate_InvalidProfile_HasNestedProfileError()
    {
        var result = _validator.Validate(Valid() with
        {
            Profile = new UpdateTenantProfileRequest
            {
                IdentityType = IdentityType.CitizenIdentityCard,
                IdentityNumber = "123"
            }
        });

        Assert.Contains(result.Errors, e => e.PropertyName == "Profile.IdentityNumber");
    }
}
