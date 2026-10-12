using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public class RoomAssetConditionHistoryMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<RoomAssetConditionHistory, RoomAssetConditionHistoryResponse>()
            .Ignore(dest => dest.Media);
    }
}
