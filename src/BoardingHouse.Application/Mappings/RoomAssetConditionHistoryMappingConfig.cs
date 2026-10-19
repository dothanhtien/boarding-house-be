using Mapster;
using BoardingHouse.Application.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Mappings;

public class RoomAssetConditionHistoryMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<RoomAssetConditionHistory, RoomAssetConditionHistoryResponse>()
            .Ignore(dest => dest.Media);
    }
}
