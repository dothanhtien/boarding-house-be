using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class RoomAssetMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateRoomAssetRequest, RoomAsset>()
        .Ignore(
            dest => dest.Name,
            dest => dest.Quantity,
            dest => dest.PurchaseDate!,
            dest => dest.PurchaseUnitPrice!,
            dest => dest.Note!)
        .AfterMapping((src, dest) =>
        {
            src.Name.ApplyIfSet(v => dest.Name = v!);
            src.Quantity.ApplyIfSet(v => dest.Quantity = v);
            src.PurchaseDate.ApplyIfSet(v => dest.PurchaseDate = v);
            src.PurchaseUnitPrice.ApplyIfSet(v => dest.PurchaseUnitPrice = v);
            src.Note.ApplyIfSet(v => dest.Note = v);
        })
        .Config;
}
