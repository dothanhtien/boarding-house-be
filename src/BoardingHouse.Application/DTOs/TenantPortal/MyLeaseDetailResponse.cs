using System.Diagnostics.CodeAnalysis;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyLeaseDetailResponse : MyLeaseResponse
{
    public MyLeaseDetailResponse() { }

    [SetsRequiredMembers]
    public MyLeaseDetailResponse(MyLeaseResponse lease) : base(lease) { }

    public List<MyLeaseRoommateResponse> Tenants { get; init; } = [];
    public List<MyLeaseUtilityServiceResponse> UtilityServices { get; init; } = [];
}
