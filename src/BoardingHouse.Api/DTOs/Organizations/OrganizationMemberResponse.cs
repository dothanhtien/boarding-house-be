namespace BoardingHouse.Api.DTOs.Organizations;

public record OrganizationMemberResponse
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string UserFullName { get; init; }
    public required Guid RoleId { get; init; }
    public required string RoleSlug { get; init; }
    public required string RoleName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
