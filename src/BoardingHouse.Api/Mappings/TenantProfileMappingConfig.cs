using Mapster;
using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Mappings;

public static class TenantProfileMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateTenantProfileRequest, TenantProfile>()
        .Ignore(
            dest => dest.IdentityType!, dest => dest.IdentityNumber!, dest => dest.IdentityIssuedDate!,
            dest => dest.IdentityIssuedPlace!, dest => dest.IdentityExpiryDate!, dest => dest.DateOfBirth!,
            dest => dest.Gender!, dest => dest.PermanentAddress!, dest => dest.EmergencyContactName!,
            dest => dest.EmergencyContactPhone!)
        .AfterMapping((src, dest) =>
        {
            src.IdentityType.ApplyIfSet(v => dest.IdentityType = v);
            src.IdentityNumber.ApplyIfSet(v => dest.IdentityNumber = NullIfBlank(v)?.ToUpperInvariant());
            src.IdentityIssuedDate.ApplyIfSet(v => dest.IdentityIssuedDate = v);
            src.IdentityIssuedPlace.ApplyIfSet(v => dest.IdentityIssuedPlace = NullIfBlank(v));
            src.IdentityExpiryDate.ApplyIfSet(v => dest.IdentityExpiryDate = v);
            src.DateOfBirth.ApplyIfSet(v => dest.DateOfBirth = v);
            src.Gender.ApplyIfSet(v => dest.Gender = v);
            src.PermanentAddress.ApplyIfSet(v => dest.PermanentAddress = NullIfBlank(v));
            src.EmergencyContactName.ApplyIfSet(v => dest.EmergencyContactName = NullIfBlank(v));
            src.EmergencyContactPhone.ApplyIfSet(v => dest.EmergencyContactPhone = NullIfBlank(v));

            if (src.IdentityNumber.IsSet && dest.IdentityNumber is null)
            {
                dest.IdentityIssuedDate = null;
                dest.IdentityIssuedPlace = null;
                dest.IdentityExpiryDate = null;
            }
        })
        .Config;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
