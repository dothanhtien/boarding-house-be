using BoardingHouse.UnitTests.Common;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class LeaseUtilityServiceTests
{
    private static LeaseUtilityService NewLeaseUtilityService(decimal defaultUnitPrice, decimal? unitPriceOverride) =>
        new LeaseUtilityService(
            Guid.NewGuid(),
            TestEntities.New<UtilityService>(new
            {
                Name = "Internet",
                Type = UtilityType.Internet,
                BillingMethod = UtilityBillingMethod.Fixed,
                Unit = "month",
                DefaultUnitPrice = defaultUnitPrice,
                CreatedBy = SentinelActors.System
            }),
            unitPriceOverride,
            quantity: 1,
            SentinelActors.System);

    // decimal is not allowed in attributes, so InlineData passes double and the test casts
    [Theory]
    [InlineData(100_000d, null, 100_000d)]
    [InlineData(100_000d, 80_000d, 80_000d)]
    [InlineData(100_000d, 0d, 0d)]
    [InlineData(0d, null, 0d)]
    public void EffectiveUnitPrice_PrefersOverrideOverDefault(double defaultPrice, double? overridePrice, double expected)
    {
        var leaseUtilityService = NewLeaseUtilityService((decimal)defaultPrice, (decimal?)overridePrice);

        Assert.Equal((decimal)expected, leaseUtilityService.EffectiveUnitPrice);
    }

    [Fact]
    public void SetQuantity_FixedService_SetsQuantity()
    {
        var leaseUtilityService = NewLeaseUtilityService(100_000, unitPriceOverride: null);

        leaseUtilityService.SetQuantity(3);

        Assert.Equal(3, leaseUtilityService.Quantity);
    }

    [Theory]
    [InlineData(UtilityBillingMethod.Metered)]
    [InlineData(UtilityBillingMethod.PerTenant)]
    public void SetQuantity_NonFixedService_OnlyAllowsOne(UtilityBillingMethod billingMethod)
    {
        var leaseUtilityService = NewLeaseUtilityService(100_000, unitPriceOverride: null);
        TestEntities.Set(leaseUtilityService.Service!, new { BillingMethod = billingMethod });

        leaseUtilityService.SetQuantity(1);

        Assert.Throws<DomainException>(() => leaseUtilityService.SetQuantity(2));
        Assert.Equal(1, leaseUtilityService.Quantity);
    }

    [Fact]
    public void EffectiveUnitPrice_ServiceNotLoaded_Throws()
    {
        var leaseUtilityService = NewLeaseUtilityService(100_000, unitPriceOverride: null);
        TestEntities.Set(leaseUtilityService, new { Service = (UtilityService?)null });

        Assert.Throws<InvalidOperationException>(() => leaseUtilityService.EffectiveUnitPrice);
    }

    [Theory]
    [InlineData(UtilityBillingMethod.Fixed, 2, 2)]
    [InlineData(UtilityBillingMethod.PerTenant, 1, 3)]
    public void ChargeFor_UsesTenantCountOnlyForPerTenant(UtilityBillingMethod billingMethod, int quantity, int expectedQuantity)
    {
        var leaseUtilityService = NewLeaseUtilityService(100_000, unitPriceOverride: 50_000);
        TestEntities.Set(leaseUtilityService.Service!, new { BillingMethod = billingMethod });
        TestEntities.Set(leaseUtilityService, new { Quantity = quantity });

        Assert.Equal(expectedQuantity, leaseUtilityService.BillableQuantity(tenantCount: 3));
        Assert.Equal(50_000m * expectedQuantity, leaseUtilityService.ChargeFor(tenantCount: 3));
    }

    [Fact]
    public void ChargeFor_MeteredService_Throws()
    {
        var leaseUtilityService = NewLeaseUtilityService(3_500, unitPriceOverride: null);
        TestEntities.Set(leaseUtilityService.Service!, new { BillingMethod = UtilityBillingMethod.Metered });

        Assert.Throws<InvalidOperationException>(() => leaseUtilityService.ChargeFor(tenantCount: 2));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void IsBillable_NeedsActiveLeaseServiceAndOfferedService(
        bool leaseServiceActive, bool serviceActive, bool serviceDeleted, bool expected)
    {
        var leaseUtilityService = NewLeaseUtilityService(100_000, unitPriceOverride: null);
        leaseUtilityService.SetIsActive(leaseServiceActive);
        TestEntities.Set(leaseUtilityService.Service!, new
        {
            IsActive = serviceActive,
            DeletedAt = serviceDeleted ? DateTimeOffset.UtcNow : (DateTimeOffset?)null
        });

        Assert.Equal(expected, leaseUtilityService.IsBillable);
    }

    [Fact]
    public void SetQuantity_Zero_Throws()
    {
        Assert.Throws<DomainException>(() => NewLeaseUtilityService(100_000, unitPriceOverride: null).SetQuantity(0));
    }
}
