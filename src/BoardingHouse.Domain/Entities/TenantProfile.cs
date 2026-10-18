using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class TenantProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public IdentityType? IdentityType { get; set; }
    public string? IdentityNumber { get; set; }
    public DateOnly? IdentityIssuedDate { get; set; }
    public string? IdentityIssuedPlace { get; set; }
    public DateOnly? IdentityExpiryDate { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? PermanentAddress { get; set; }

    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    public bool IsEmpty() =>
        this is
        {
            IdentityType: null,
            IdentityNumber: null,
            IdentityIssuedDate: null,
            IdentityIssuedPlace: null,
            IdentityExpiryDate: null,
            DateOfBirth: null,
            Gender: null,
            PermanentAddress: null,
            EmergencyContactName: null,
            EmergencyContactPhone: null
        };
}
