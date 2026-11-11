using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class UtilityServiceTests
{
    private static UtilityServiceDetails Details(UtilityBillingMethod billingMethod = UtilityBillingMethod.Fixed) =>
        new("Internet", billingMethod, "month", DefaultUnitPrice: 100_000, IsActive: true);

    private static UtilityService NewService() =>
        UtilityService.Create(Guid.NewGuid(), UtilityType.Internet, Details(), SentinelActors.System);

    [Theory]
    [InlineData(UtilityServiceUsage.Leases, "used by a lease")]
    [InlineData(UtilityServiceUsage.Readings, "has utility readings")]
    public void Update_BillingMethodOfAUsedService_ThrowsConflict(UtilityServiceUsage usage, string reason)
    {
        var service = NewService();

        var ex = Assert.Throws<DomainException>(() => service.Update(Details(UtilityBillingMethod.PerTenant), usage));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Update_OtherFieldsOfAUsedService_IsAllowed()
    {
        var service = NewService();

        service.Update(Details() with { DefaultUnitPrice = 120_000 }, UtilityServiceUsage.Leases | UtilityServiceUsage.Readings);

        Assert.Equal(120_000, service.DefaultUnitPrice);
    }

    [Fact]
    public void Update_BillingMethodOfAnUnusedService_Changes()
    {
        var service = NewService();

        service.Update(Details(UtilityBillingMethod.PerTenant), UtilityServiceUsage.None);

        Assert.Equal(UtilityBillingMethod.PerTenant, service.BillingMethod);
    }

    [Fact]
    public void Create_NegativePrice_Throws()
    {
        Assert.Throws<DomainException>(() => UtilityService.Create(
            Guid.NewGuid(), UtilityType.Other, Details() with { DefaultUnitPrice = -1 }, SentinelActors.System));
    }
}
