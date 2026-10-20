using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Infrastructure.Persistence.Converters;

public static class MediaAssetEntityTypeDbValueConverter
{
    public static readonly ValueConverter<MediaAssetEntityType, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<MediaAssetEntityType, string>
        {
            [MediaAssetEntityType.UserAvatar] = "USER_AVATAR",
            [MediaAssetEntityType.OrganizationLogo] = "ORGANIZATION_LOGO",
            [MediaAssetEntityType.PropertyMedia] = "PROPERTY_MEDIA",
            [MediaAssetEntityType.RoomMedia] = "ROOM_MEDIA",
            [MediaAssetEntityType.RoomAssetConditionPhoto] = "ROOM_ASSET_CONDITION_PHOTO"
        });
}
