namespace BoardingHouse.Api.DTOs.Leases;

public record CreateLeaseTenantRequest
{
    public required Guid UserId { get; init; }
    public bool IsRepresentative { get; init; }
    public DateOnly? JoinedDate { get; init; }
}
