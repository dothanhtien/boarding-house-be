using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Tenants;

public record TenantLeaseResponse
{
    public required Guid LeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public required LeaseStatus Status { get; init; }
    public required DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}
