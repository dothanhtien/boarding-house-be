using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Organizations;

public record UpdateOrganizationSettingsRequest
{
    public int? DefaultBillingDay { get; init; }
    public LateFeeType? LateFeeType { get; init; }
    public decimal? LateFeeValue { get; init; }
    public int? LateFeeGraceDays { get; init; }
    public decimal? VatRate { get; init; }
    public string? BankAccountNumber { get; init; }
    public string? BankName { get; init; }
    public string? BankAccountName { get; init; }
}
