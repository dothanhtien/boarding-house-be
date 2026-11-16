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
            new OrganizationSettingsDetails(
                10, new LateFeePolicy(LateFeeType.Fixed, 20_000, 2), VatRate: 8, "123", "VCB", "A", AutoGenerateInvoices: true),
            actor,
            now);

        Assert.Equal((10, LateFeeType.Fixed, 20_000m, 8m, actor, now),
            (settings.DefaultBillingDay, settings.LateFeeType, settings.LateFeeValue, settings.VatRate, settings.UpdatedBy, settings.UpdatedAt));
        Assert.True(settings.AutoGenerateInvoices);
    }

    [Fact]
    public void OrganizationSettingsUpdate_VatRateAbove100_Throws()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());

        Assert.Throws<DomainException>(() => settings.Update(
            new OrganizationSettingsDetails(null, LateFeePolicy.None, VatRate: 101, null, null, null, AutoGenerateInvoices: false),
            Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void OrganizationSettingsDefaults_AutoGenerateInvoicesIsOff()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());

        Assert.False(settings.AutoGenerateInvoices);
        Assert.False(settings.Details.AutoGenerateInvoices);
    }

    [Fact]
    public void OrganizationSettingsUpdate_DetailsRoundTrip_KeepsAutoGenerateInvoices()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());
        settings.Update(settings.Details with { AutoGenerateInvoices = true }, Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.Update(settings.Details with { VatRate = 8 }, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.True(settings.AutoGenerateInvoices);
        Assert.True(settings.Details.AutoGenerateInvoices);
    }

    [Fact]
    public void OrganizationSettingsUpdate_AutoGenerateInvoicesFalse_TurnsItOff()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());
        settings.Update(settings.Details with { AutoGenerateInvoices = true }, Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.Update(settings.Details with { AutoGenerateInvoices = false }, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.False(settings.AutoGenerateInvoices);
    }

    [Fact]
    public void OrganizationSettingsUpdate_InvalidDetails_DoesNotTurnOnAutoGenerateInvoices()
    {
        var settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());

        Assert.Throws<DomainException>(() => settings.Update(
            settings.Details with { VatRate = 101, AutoGenerateInvoices = true }, Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.False(settings.AutoGenerateInvoices);
    }

    [Theory]
    [InlineData(LateFeeType.Percent, 5d, 3_050_000, 152_500)]
    [InlineData(LateFeeType.Percent, 3.33d, 5_355_000, 178_322)] // 178,321.5 rounded away from zero
    [InlineData(LateFeeType.Percent, 0d, 3_050_000, 0)]
    [InlineData(LateFeeType.Fixed, 100_000d, 3_050_000, 100_000)]
    [InlineData(LateFeeType.Fixed, 0d, 3_050_000, 0)]
    public void FeeFor_ReturnsTheRoundedPercentOrTheFixedAmount(LateFeeType type, double value, int totalAmount, int expected)
    {
        var policy = new LateFeePolicy(type, (decimal)value, graceDays: null);

        Assert.Equal(expected, policy.FeeFor(totalAmount));
    }

    [Fact]
    public void FeeFor_NoPolicy_ReturnsZero()
    {
        Assert.Equal(0, LateFeePolicy.None.FeeFor(3_050_000));
    }

    [Fact]
    public void FeeFor_GraceDaysOnly_ReturnsZero()
    {
        Assert.Equal(0, new LateFeePolicy(null, null, graceDays: 5).FeeFor(3_050_000));
    }
}
