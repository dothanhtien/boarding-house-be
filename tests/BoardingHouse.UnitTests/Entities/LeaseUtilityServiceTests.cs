using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class LeaseUtilityServiceTests
{
    private static LeaseUtilityService NewLeaseUtilityService(decimal? defaultUnitPrice, decimal? unitPriceOverride) => new()
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
    [InlineData(null, 80_000d, 80_000d)]
    [InlineData(100_000d, 0d, 0d)]
    public void EffectiveUnitPrice_PrefersOverrideOverDefault(double? defaultPrice, double? overridePrice, double expected)
    {
        var leaseUtilityService = NewLeaseUtilityService((decimal?)defaultPrice, (decimal?)overridePrice);

        Assert.Equal((decimal)expected, leaseUtilityService.EffectiveUnitPrice);
    }

    [Fact]
    public void EffectiveUnitPrice_NoOverrideAndNoDefault_IsNull()
    {
        var leaseUtilityService = NewLeaseUtilityService(defaultUnitPrice: null, unitPriceOverride: null);

        Assert.Null(leaseUtilityService.EffectiveUnitPrice);
    }

    [Fact]
    public void EffectiveUnitPrice_ServiceNotLoaded_Throws()
    {
        var leaseUtilityService = new LeaseUtilityService { CreatedBy = SentinelActors.System };

        Assert.Throws<InvalidOperationException>(() => leaseUtilityService.EffectiveUnitPrice);
    }
}
