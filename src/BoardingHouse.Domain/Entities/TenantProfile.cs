using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class TenantProfile : BaseEntity
{
    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public IdentityType? IdentityType { get; private set; }
    public string? IdentityNumber { get; private set; }
    public DateOnly? IdentityIssuedDate { get; private set; }
    public string? IdentityIssuedPlace { get; private set; }
    public DateOnly? IdentityExpiryDate { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }
    public Gender? Gender { get; private set; }
    public string? PermanentAddress { get; private set; }

    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }

    public TenantProfileDetails Details => new(
        IdentityType,
        IdentityNumber,
        IdentityIssuedDate,
        IdentityIssuedPlace,
        IdentityExpiryDate,
        DateOfBirth,
        Gender,
        PermanentAddress,
        EmergencyContactName,
        EmergencyContactPhone);

    private TenantProfile() { }

    public static TenantProfile Create(Guid userId, TenantProfileDetails details, Guid createdBy)
    {
        var profile = new TenantProfile { UserId = userId, CreatedBy = createdBy };
        profile.Update(details);
        return profile;
    }

    public void Update(TenantProfileDetails details)
    {
        var normalized = Normalize(details);

        if (normalized == TenantProfileDetails.Empty)
        {
            throw new DomainException("Tenant profile must contain at least one field");
        }

        if (normalized.IdentityNumber is null
            && (normalized.IdentityIssuedDate is not null
                || normalized.IdentityIssuedPlace is not null
                || normalized.IdentityExpiryDate is not null))
        {
            throw new DomainException("Identity issued date, issued place and expiry date require an identity document");
        }

        if (normalized is { IdentityIssuedDate: { } issued, IdentityExpiryDate: { } expiry } && expiry <= issued)
        {
            throw new DomainException("Identity expiry date must be after the identity issued date");
        }

        IdentityType = normalized.IdentityType;
        IdentityNumber = normalized.IdentityNumber;
        IdentityIssuedDate = normalized.IdentityIssuedDate;
        IdentityIssuedPlace = normalized.IdentityIssuedPlace;
        IdentityExpiryDate = normalized.IdentityExpiryDate;
        DateOfBirth = normalized.DateOfBirth;
        Gender = normalized.Gender;
        PermanentAddress = normalized.PermanentAddress;
        EmergencyContactName = normalized.EmergencyContactName;
        EmergencyContactPhone = normalized.EmergencyContactPhone;
    }

    private static TenantProfileDetails Normalize(TenantProfileDetails details) => details with
    {
        IdentityNumber = NullIfBlank(details.IdentityNumber)?.ToUpperInvariant(),
        IdentityIssuedPlace = NullIfBlank(details.IdentityIssuedPlace),
        PermanentAddress = NullIfBlank(details.PermanentAddress),
        EmergencyContactName = NullIfBlank(details.EmergencyContactName),
        EmergencyContactPhone = NullIfBlank(details.EmergencyContactPhone)
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
