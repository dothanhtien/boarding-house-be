using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardingHouse.Api.Persistence.Converters;

public static class StorageProviderDbValueConverter
{
    public static readonly ValueConverter<StorageProvider, string> Instance = EnumDbValueConverter.Create(
        new Dictionary<StorageProvider, string>
        {
            [StorageProvider.Cloudinary] = "CLOUDINARY"
        });
}
