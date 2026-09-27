using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class MediaAssetEntityTypeDbValueConverter
{
    public static readonly ValueConverter<MediaAssetEntityType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<MediaAssetEntityType, string>
        {
            [MediaAssetEntityType.UserAvatar] = "USER_AVATAR",
            [MediaAssetEntityType.OrganizationLogo] = "ORGANIZATION_LOGO",
            [MediaAssetEntityType.PropertyMedia] = "PROPERTY_MEDIA"
        });
}
