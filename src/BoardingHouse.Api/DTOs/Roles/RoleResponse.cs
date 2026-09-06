namespace BoardingHouse.Api.DTOs.Roles;

public record RoleResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }
}
