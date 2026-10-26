using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class LeaseUtilityServiceTests
{
    private static LeaseUtilityService NewLeaseUtilityService(decimal defaultUnitPrice, decimal? unitPriceOverride) => new()
    {
        CreatedBy = SentinelActors.System,
        UnitPriceOverride = unitPriceOverride,
        Service = new UtilityService
        {
            Name = "Internet",
            Type = UtilityType.Internet,
            BillingMethod = UtilityBillingMethod.Fixed,
            Unit = "month",
            DefaultUnitPrice = defaultUnitPrice,
            CreatedBy = SentinelActors.System
        }
    };

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
        leaseUtilityService.Service!.BillingMethod = billingMethod;

        leaseUtilityService.SetQuantity(1);

        Assert.Throws<DomainException>(() => leaseUtilityService.SetQuantity(2));
        Assert.Equal(1, leaseUtilityService.Quantity);
    }

    [Fact]
    public void EffectiveUnitPrice_ServiceNotLoaded_Throws()
    {
        var leaseUtilityService = new LeaseUtilityService { CreatedBy = SentinelActors.System };

        Assert.Throws<InvalidOperationException>(() => leaseUtilityService.EffectiveUnitPrice);
    }
}
