namespace BoardingHouse.Api.DTOs.Users;

public record UserOrganizationResponse
{
    public required Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required Guid RoleId { get; init; }
    public required string RoleSlug { get; init; }
    public required string RoleName { get; init; }
}
