namespace BoardingHouse.Api.DTOs.Organizations;

public record UpdateOrganizationMemberRequest
{
    public required Guid RoleId { get; init; }
}
