namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyLeaseRoommateResponse
{
    public required string FullName { get; init; }
    public required bool IsRepresentative { get; init; }
    public required DateOnly JoinedDate { get; init; }
    public DateOnly? LeftDate { get; init; }
}
