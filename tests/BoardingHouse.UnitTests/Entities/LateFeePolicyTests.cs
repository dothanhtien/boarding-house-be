using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class LateFeePolicyTests
{
    [Theory]
    [InlineData(LateFeeType.Fixed, null)]
    [InlineData(null, 10d)]
    public void New_TypeWithoutValue_Throws(LateFeeType? type, double? value)
    {
        var ex = Assert.Throws<DomainException>(() => new LateFeePolicy(type, (decimal?)value, graceDays: null));

        Assert.Equal("LateFeeType and LateFeeValue must be set together", ex.Message);
    }

    [Fact]
    public void New_PercentAbove100_Throws()
    {
        Assert.Throws<DomainException>(() => new LateFeePolicy(LateFeeType.Percent, 101, graceDays: null));
    }

    [Fact]
    public void New_FixedAbove100_IsAllowed()
    {
        Assert.Equal(50_000, new LateFeePolicy(LateFeeType.Fixed, 50_000, graceDays: 3).Value);
    }

    [Fact]
    public void New_GraceDaysWithoutLateFee_IsAllowed()
    {
        Assert.Equal(5, new LateFeePolicy(null, null, graceDays: 5).GraceDays);
    }

    [Fact]
    public void PropertyUpdate_PatchOfOnlyTheValue_IsCheckedOnTheMergedPolicy()
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Nhà A", null, null, null, null, null, true, DefaultBillingDay: 5,
                new LateFeePolicy(LateFeeType.Percent, 5, graceDays: 3)),
            SentinelActors.System);
        var current = property.Details;

        Assert.Throws<DomainException>(() =>
            property.Update(current with { LateFee = new LateFeePolicy(current.LateFee.Type, 150, current.LateFee.GraceDays) }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void PropertyCreate_BillingDayOutOfRange_Throws(int billingDay)
    {
        Assert.Throws<DomainException>(() => Property.Create(
            Guid.NewGuid(), new PropertyDetails("Nhà A", null, null, null, null, null, true, billingDay, LateFeePolicy.None),
            SentinelActors.System));
    }

    [Fact]
    public void OrganizationSettingsUpdate_SetsFieldsAndAuditColumns()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());
        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        settings.Update(
            new OrganizationSettingsDetails(10, new LateFeePolicy(LateFeeType.Fixed, 20_000, 2), VatRate: 8, "123", "VCB", "A"),
            actor,
            now);

        Assert.Equal((10, LateFeeType.Fixed, 20_000m, 8m, actor, now),
            (settings.DefaultBillingDay, settings.LateFeeType, settings.LateFeeValue, settings.VatRate, settings.UpdatedBy, settings.UpdatedAt));
    }

    [Fact]
    public void OrganizationSettingsUpdate_VatRateAbove100_Throws()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());

        Assert.Throws<DomainException>(() => settings.Update(
            new OrganizationSettingsDetails(null, LateFeePolicy.None, VatRate: 101, null, null, null), Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
