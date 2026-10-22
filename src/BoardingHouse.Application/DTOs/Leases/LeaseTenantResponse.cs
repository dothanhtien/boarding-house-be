namespace BoardingHouse.Application.DTOs.Leases;

public record LeaseTenantResponse
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string FullName { get; init; }
    public string? Phone { get; init; }
    public required bool IsRepresentative { get; init; }
    public DateOnly? JoinedDate { get; init; }
    public DateOnly? LeftDate { get; init; }
}
