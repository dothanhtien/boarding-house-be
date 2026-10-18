using FluentValidation;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.DTOs.TenantProfiles;

public class UpdateTenantProfileRequestValidator : AbstractValidator<UpdateTenantProfileRequest>
{
    public const int MaxAgeYears = 120;

    public UpdateTenantProfileRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.IdentityType.IsSet == x.IdentityNumber.IsSet
                && (x.IdentityType.Value is null) == string.IsNullOrWhiteSpace(x.IdentityNumber.Value))
            .WithMessage("Identity type and Identity number must be set together")
            .OverridePropertyName(nameof(UpdateTenantProfileRequest.IdentityNumber));

        RuleFor(x => x)
            .Must(x => !HasIssuanceDetails(x))
            .WithMessage("Identity issued date, issued place and expiry date cannot be set while clearing the identity document")
            .When(x => x.IdentityNumber.IsSet && string.IsNullOrWhiteSpace(x.IdentityNumber.Value))
            .OverridePropertyName(nameof(UpdateTenantProfileRequest.IdentityNumber));

        RuleFor(x => x.IdentityType.Value)
            .IsInEnum().WithMessage("Identity type is invalid")
            .When(x => x.IdentityType is { IsSet: true, Value: not null })
            .OverridePropertyName("IdentityType");

        RuleFor(x => x.IdentityNumber.Value)
            .Must(v => v!.Trim().Length <= 20).WithMessage("Identity number exceeds 20 characters")
            .When(x => x.IdentityNumber is { IsSet: true, Value: not null })
            .OverridePropertyName("IdentityNumber");

        RuleFor(x => x.IdentityNumber.Value)
            .Matches(@"^\s*\d{12}\s*$").WithMessage("Citizen identity card number must be exactly 12 digits")
            .When(x => x.IdentityType.Value == IdentityType.CitizenIdentityCard && x.IdentityNumber.Value is not null)
            .OverridePropertyName("IdentityNumber");

        RuleFor(x => x.IdentityNumber.Value)
            .Matches(@"^\s*[A-Za-z0-9]{6,20}\s*$").WithMessage("Passport number must be 6 to 20 letters or digits")
            .When(x => x.IdentityType.Value == IdentityType.Passport && x.IdentityNumber.Value is not null)
            .OverridePropertyName("IdentityNumber");

        RuleFor(x => x.IdentityNumber.Value)
            .Matches(@"^\s*[A-Za-z0-9]{4,20}\s*$").WithMessage("Identity number must be 4 to 20 letters or digits")
            .When(x => x.IdentityType.Value == IdentityType.Other && x.IdentityNumber.Value is not null)
            .OverridePropertyName("IdentityNumber");

        RuleFor(x => x.IdentityIssuedDate.Value)
            .LessThanOrEqualTo(_ => Today()).WithMessage("Identity issued date must not be in the future")
            .When(x => x.IdentityIssuedDate is { IsSet: true, Value: not null })
            .OverridePropertyName("IdentityIssuedDate");

        RuleFor(x => x.IdentityIssuedPlace.Value)
            .MaximumLength(100).WithMessage("Identity issued place exceeds 100 characters")
            .When(x => x.IdentityIssuedPlace is { IsSet: true, Value: not null })
            .OverridePropertyName("IdentityIssuedPlace");

        RuleFor(x => x.IdentityExpiryDate.Value)
            .GreaterThan(x => x.IdentityIssuedDate.Value).WithMessage("Identity expiry date must be after the identity issued date")
            .When(x => x.IdentityExpiryDate is { IsSet: true, Value: not null } && x.IdentityIssuedDate is { IsSet: true, Value: not null })
            .OverridePropertyName("IdentityExpiryDate");

        RuleFor(x => x.DateOfBirth.Value)
            .LessThanOrEqualTo(_ => Today()).WithMessage("Date of birth must not be in the future")
            .GreaterThanOrEqualTo(_ => Today().AddYears(-MaxAgeYears)).WithMessage($"Date of birth must be within the last {MaxAgeYears} years")
            .When(x => x.DateOfBirth is { IsSet: true, Value: not null })
            .OverridePropertyName("DateOfBirth");

        RuleFor(x => x.Gender.Value)
            .IsInEnum().WithMessage("Gender is invalid")
            .When(x => x.Gender is { IsSet: true, Value: not null })
            .OverridePropertyName("Gender");

        RuleFor(x => x.PermanentAddress.Value)
            .MaximumLength(255).WithMessage("Permanent address exceeds 255 characters")
            .When(x => x.PermanentAddress is { IsSet: true, Value: not null })
            .OverridePropertyName("PermanentAddress");

        RuleFor(x => x.EmergencyContactName.Value)
            .MaximumLength(100).WithMessage("Emergency contact name exceeds 100 characters")
            .When(x => x.EmergencyContactName is { IsSet: true, Value: not null })
            .OverridePropertyName("EmergencyContactName");

        RuleFor(x => x.EmergencyContactPhone.Value)
            .Must(v => v!.Trim().Length <= 20).WithMessage("Emergency contact phone exceeds 20 characters")
            .Matches(@"^\s*(\+?\d+)?\s*$").WithMessage("Emergency contact phone must contain only digits, optionally starting with +")
            .When(x => x.EmergencyContactPhone is { IsSet: true, Value: not null })
            .OverridePropertyName("EmergencyContactPhone");
    }

    private static bool HasIssuanceDetails(UpdateTenantProfileRequest x) =>
        x.IdentityIssuedDate is { IsSet: true, Value: not null }
        || (x.IdentityIssuedPlace.IsSet && !string.IsNullOrWhiteSpace(x.IdentityIssuedPlace.Value))
        || x.IdentityExpiryDate is { IsSet: true, Value: not null };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
