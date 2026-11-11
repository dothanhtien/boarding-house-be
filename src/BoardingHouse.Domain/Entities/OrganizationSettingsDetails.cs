namespace BoardingHouse.Domain.Entities;

public sealed record OrganizationSettingsDetails(
    int? DefaultBillingDay,
    LateFeePolicy LateFee,
    decimal? VatRate,
    string? BankAccountNumber,
    string? BankName,
    string? BankAccountName);
