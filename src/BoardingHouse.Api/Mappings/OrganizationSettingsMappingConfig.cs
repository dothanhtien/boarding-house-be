using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using Mapster;

namespace BoardingHouse.Api.Mappings;

public static class OrganizationSettingsMappingConfig
{
    public static readonly TypeAdapterConfig UpdateConfig = new TypeAdapterConfig()
        .NewConfig<UpdateOrganizationSettingsRequest, OrganizationSettings>()
        .Ignore(
            dest => dest.DefaultBillingDay!, dest => dest.LateFeeType!, dest => dest.LateFeeValue!,
            dest => dest.LateFeeGraceDays!, dest => dest.VatRate!, dest => dest.BankAccountNumber!,
            dest => dest.BankName!, dest => dest.BankAccountName!)
        .AfterMapping((src, dest) =>
        {
            src.DefaultBillingDay.ApplyIfSet(v => dest.DefaultBillingDay = v);
            src.LateFeeType.ApplyIfSet(v => dest.LateFeeType = v);
            src.LateFeeValue.ApplyIfSet(v => dest.LateFeeValue = v);
            src.LateFeeGraceDays.ApplyIfSet(v => dest.LateFeeGraceDays = v);
            src.VatRate.ApplyIfSet(v => dest.VatRate = v);
            src.BankAccountNumber.ApplyIfSet(v => dest.BankAccountNumber = v);
            src.BankName.ApplyIfSet(v => dest.BankName = v);
            src.BankAccountName.ApplyIfSet(v => dest.BankAccountName = v);
        })
        .Config;
}
