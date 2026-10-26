namespace BoardingHouse.Application.DTOs.Leases;

public record CreateLeaseRequest
{
    public required Guid RoomId { get; init; }
    public Guid? PreviousLeaseId { get; init; }
    public string? LeaseCode { get; init; }
    public required DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int? BillingDay { get; init; }
    public decimal? MonthlyRent { get; init; }
    public decimal? DepositAmount { get; init; }

    public List<CreateLeaseTenantRequest>? Tenants { get; init; }
    public List<CreateLeaseUtilityServiceRequest>? UtilityServices { get; init; }
}
