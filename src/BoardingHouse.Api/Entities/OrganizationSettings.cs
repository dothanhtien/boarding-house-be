using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class OrganizationSettings
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int? DefaultBillingDay { get; set; }
    public LateFeeType? LateFeeType { get; set; }
    public decimal? LateFeeValue { get; set; }
    public int? LateFeeGraceDays { get; set; }

    public decimal? VatRate { get; set; }
    public string Currency { get; set; } = "VND";

    public string? BankAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountName { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
