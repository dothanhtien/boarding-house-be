using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.Leases;

public record LeaseStatusHistoryResponse
{
    public required Guid Id { get; init; }
    public LeaseStatus? OldStatus { get; init; }
    public required LeaseStatus NewStatus { get; init; }
    public string? Reason { get; init; }
    public Guid? ChangedBy { get; init; }
    public required DateTimeOffset ChangedAt { get; init; }
}
