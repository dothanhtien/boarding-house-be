using BoardingHouse.Api.DTOs.Leases;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateLeaseRequestTests
{
    [Fact]
    public void Fields_AreAllHandledByHasContractChanges()
    {
        string[] expected =
        [
            nameof(UpdateLeaseRequest.PreviousLeaseId),
            nameof(UpdateLeaseRequest.LeaseCode),
            nameof(UpdateLeaseRequest.StartDate),
            nameof(UpdateLeaseRequest.EndDate),
            nameof(UpdateLeaseRequest.BillingDay),
            nameof(UpdateLeaseRequest.MonthlyRent),
            nameof(UpdateLeaseRequest.DepositAmount),
            nameof(UpdateLeaseRequest.Tenants)
        ];

        var actual = typeof(UpdateLeaseRequest).GetProperties().Select(p => p.Name);

        Assert.Equal(expected.Order(), actual.Order());
    }
}
