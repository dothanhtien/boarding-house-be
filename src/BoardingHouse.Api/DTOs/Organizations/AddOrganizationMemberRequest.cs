namespace BoardingHouse.Api.DTOs.Organizations;

public record AddOrganizationMemberRequest
{
    public required Guid UserId { get; init; }
    public required Guid RoleId { get; init; }
}
