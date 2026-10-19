using Mapster;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Mappings;

public static class LeaseMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateLeaseRequest, Lease>()
        .Ignore(
            dest => dest.PreviousLeaseId!,
            dest => dest.LeaseCode,
            dest => dest.StartDate,
            dest => dest.EndDate!,
            dest => dest.BillingDay!,
            dest => dest.MonthlyRent,
            dest => dest.DepositAmount,
            dest => dest.Tenants)
        .AfterMapping((src, dest) =>
        {
            src.PreviousLeaseId.ApplyIfSet(v => dest.PreviousLeaseId = v);
            src.LeaseCode.ApplyIfSet(v => dest.LeaseCode = v!);
            src.StartDate.ApplyIfSet(v => dest.StartDate = v);
            src.EndDate.ApplyIfSet(v => dest.EndDate = v);
            src.BillingDay.ApplyIfSet(v => dest.BillingDay = v);
            src.MonthlyRent.ApplyIfSet(v => dest.MonthlyRent = v);
            src.DepositAmount.ApplyIfSet(v => dest.DepositAmount = v);
        })
        .Config;
}
