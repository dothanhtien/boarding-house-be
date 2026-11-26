namespace BoardingHouse.Application.DTOs.TenantPortal;

public record TenancyResponse
{
    public required int ActiveLeaseCount { get; init; }
    public required int TotalLeaseCount { get; init; }
}
