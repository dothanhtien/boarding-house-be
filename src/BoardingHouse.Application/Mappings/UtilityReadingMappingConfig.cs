using Mapster;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Mappings;

public class UtilityReadingMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<BillingMonth, DateOnly>().MapWith(month => month.FirstDay);

        config.NewConfig<UtilityReading, UtilityReadingResponse>()
            .Map(dest => dest.ServiceName, src => src.Service!.Name)
            .Map(dest => dest.Unit, src => src.Service!.Unit)
            .Map(dest => dest.BilledLeaseCode, src => src.BilledLease == null ? null : src.BilledLease.LeaseCode)
            .Ignore(dest => dest.ImageUrl!);

        config.NewConfig<Lease, UtilityReadingRoomLeaseResponse>()
            .Map(dest => dest.LeaseId, src => src.Id);
    }
}
