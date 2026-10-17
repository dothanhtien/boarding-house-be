using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateTenantProfileRequestValidatorTests
{
    private readonly UpdateTenantProfileRequestValidator _validator = new();

    private static UpdateTenantProfileRequest ValidRequest() => new()
    {
        IdentityType = IdentityType.CitizenIdentityCard,
        IdentityNumber = "001095012345"
    };

    [Fact]
    public void Validate_EmptyRequest_IsValid()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_IdentityTypeAndNumberBothSet_IsValid()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_IdentityTypeAndNumberBothNull_IsValid()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = null, IdentityNumber = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_OnlyIdentityTypeSent_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = IdentityType.Passport });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_OnlyIdentityNumberSent_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityNumber = "001095012345" });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_IdentityTypeSetButNumberBlank_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { IdentityNumber = "   " });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_IdentityTypeNullButNumberSet_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { IdentityType = null });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_IdentityTypeOutOfRange_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { IdentityType = (IdentityType)99 });

        result.ShouldHaveValidationErrorFor(x => x.IdentityType);
    }

    [Fact]
    public void Validate_IdentityNumberWithSurroundingSpaces_IsValid()
    {
        var result = _validator.TestValidate(ValidRequest() with { IdentityNumber = "  001095012345 " });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("00109501234")]
    [InlineData("0010950123456")]
    [InlineData("00109501234A")]
    public void Validate_CitizenIdentityCardNotTwelveDigits_HasError(string number)
    {
        var result = _validator.TestValidate(ValidRequest() with { IdentityNumber = number });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Theory]
    [InlineData("B12345")]
    [InlineData("c1234567")]
    public void Validate_PassportAlphanumeric_HasNoError(string number)
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = IdentityType.Passport, IdentityNumber = number });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("B1234")]
    [InlineData("B-123456")]
    public void Validate_PassportInvalid_HasError(string number)
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = IdentityType.Passport, IdentityNumber = number });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_OtherIdentityNumberTooLong_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = IdentityType.Other, IdentityNumber = new string('x', 21) });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Theory]
    [InlineData("AB 1234")]
    [InlineData("AB-1234")]
    [InlineData("AB.1234")]
    [InlineData("AB1")]
    public void Validate_OtherIdentityNumberNotPlainAlphanumeric_HasError(string number)
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = IdentityType.Other, IdentityNumber = number });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_OtherIdentityNumberAlphanumeric_IsValid()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityType = IdentityType.Other, IdentityNumber = " AB1234 " });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ClearIdentityWhileSettingIssuanceDetails_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest
        {
            IdentityType = null,
            IdentityNumber = null,
            IdentityIssuedPlace = "Ha Noi"
        });

        result.ShouldHaveValidationErrorFor(x => x.IdentityNumber);
    }

    [Fact]
    public void Validate_ClearIdentityAndIssuanceDetailsTogether_IsValid()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest
        {
            IdentityType = null,
            IdentityNumber = null,
            IdentityIssuedDate = null,
            IdentityIssuedPlace = "",
            IdentityExpiryDate = null
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ExpiryNotAfterIssuedDate_HasError(int daysAfterIssued)
    {
        var issued = new DateOnly(2021, 5, 1);
        var result = _validator.TestValidate(ValidRequest() with
        {
            IdentityIssuedDate = issued,
            IdentityExpiryDate = issued.AddDays(daysAfterIssued)
        });

        result.ShouldHaveValidationErrorFor(x => x.IdentityExpiryDate);
    }

    [Fact]
    public void Validate_DateOfBirthTooFarInPast_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest
        {
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-UpdateTenantProfileRequestValidator.MaxAgeYears).AddDays(-1)
        });

        result.ShouldHaveValidationErrorFor(x => x.DateOfBirth);
    }

    [Fact]
    public void Validate_PermanentAddressTooLong_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { PermanentAddress = new string('x', 256) });

        result.ShouldHaveValidationErrorFor(x => x.PermanentAddress);
    }

    [Fact]
    public void Validate_DateOfBirthInFuture_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest
        {
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });

        result.ShouldHaveValidationErrorFor(x => x.DateOfBirth);
    }

    [Fact]
    public void Validate_IdentityIssuedDateInFuture_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest
        {
            IdentityIssuedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });

        result.ShouldHaveValidationErrorFor(x => x.IdentityIssuedDate);
    }

    [Fact]
    public void Validate_GenderOutOfRange_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { Gender = (Gender)99 });

        result.ShouldHaveValidationErrorFor(x => x.Gender);
    }

    [Fact]
    public void Validate_EmergencyContactPhoneWithLetters_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { EmergencyContactPhone = "09abc" });

        result.ShouldHaveValidationErrorFor(x => x.EmergencyContactPhone);
    }

    [Fact]
    public void Validate_EmergencyContactPhoneBlank_IsValid()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { EmergencyContactPhone = "   " });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_IdentityIssuedPlaceTooLong_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { IdentityIssuedPlace = new string('x', 101) });

        result.ShouldHaveValidationErrorFor(x => x.IdentityIssuedPlace);
    }

    [Fact]
    public void Validate_EmergencyContactNameTooLong_HasError()
    {
        var result = _validator.TestValidate(new UpdateTenantProfileRequest { EmergencyContactName = new string('x', 101) });

        result.ShouldHaveValidationErrorFor(x => x.EmergencyContactName);
    }
}
