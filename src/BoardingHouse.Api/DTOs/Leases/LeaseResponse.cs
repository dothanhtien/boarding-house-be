using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Leases;

public record LeaseResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid PropertyId { get; init; }
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public Guid? PreviousLeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int? BillingDay { get; init; }
    public required decimal MonthlyRent { get; init; }
    public required decimal DepositAmount { get; init; }
    public required LeaseStatus Status { get; init; }
    public DateTimeOffset? SignedAt { get; init; }
    public DateTimeOffset? TerminatedAt { get; init; }
    public string? TerminationReason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public required List<LeaseTenantResponse> Tenants { get; init; }
}
