namespace BoardingHouse.Api.DTOs.Users;

public record UpdateUserRequest
{
    public string? Phone { get; init; }
    public required string FullName { get; init; }
    public required bool IsActive { get; init; }
}
