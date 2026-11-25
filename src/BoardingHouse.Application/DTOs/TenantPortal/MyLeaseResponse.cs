using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyLeaseResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required Guid PropertyId { get; init; }
    public required string PropertyName { get; init; }
    public string? PropertyAddress { get; init; }
    public required Guid RoomId { get; init; }
    public required string RoomNumber { get; init; }
    public required string LeaseCode { get; init; }
    public required LeaseStatus Status { get; init; }
    public required DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int? BillingDay { get; init; }
    public required decimal MonthlyRent { get; init; }
    public required decimal DepositAmount { get; init; }
    public DateTimeOffset? SignedAt { get; init; }
    public DateTimeOffset? TerminatedAt { get; init; }

    public required bool IsRepresentative { get; init; }
    public required List<MyStayResponse> MyStays { get; init; }
}
