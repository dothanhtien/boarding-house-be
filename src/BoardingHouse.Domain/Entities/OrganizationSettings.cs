using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class OrganizationSettings
{
    public Guid OrganizationId { get; private set; }
    public Organization? Organization { get; private set; }

    public int? DefaultBillingDay { get; private set; }
    public LateFeeType? LateFeeType { get; private set; }
    public decimal? LateFeeValue { get; private set; }
    public int? LateFeeGraceDays { get; private set; }

    public decimal? VatRate { get; private set; }
    public string Currency { get; private set; } = "VND";

    public string? BankAccountNumber { get; private set; }
    public string? BankName { get; private set; }
    public string? BankAccountName { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public LateFeePolicy LateFee => new(LateFeeType, LateFeeValue, LateFeeGraceDays);

    public OrganizationSettingsDetails Details =>
        new(DefaultBillingDay, LateFee, VatRate, BankAccountNumber, BankName, BankAccountName);

    private OrganizationSettings() { }

    public static OrganizationSettings DefaultsFor(Guid organizationId) => new() { OrganizationId = organizationId };

    public void Update(OrganizationSettingsDetails details, Guid updatedBy, DateTimeOffset now)
    {
        BillingDayRule.EnsureValid(details.DefaultBillingDay, nameof(DefaultBillingDay));

        if (details.VatRate is < 0 or > 100)
        {
            throw new DomainException("VatRate must be between 0 and 100");
        }

        DefaultBillingDay = details.DefaultBillingDay;
        LateFeeType = details.LateFee.Type;
        LateFeeValue = details.LateFee.Value;
        LateFeeGraceDays = details.LateFee.GraceDays;
        VatRate = details.VatRate;
        BankAccountNumber = details.BankAccountNumber;
        BankName = details.BankName;
        BankAccountName = details.BankAccountName;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
