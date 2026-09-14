namespace BoardingHouse.Api.DTOs.Users;

public record UpdateUserRequest
{
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? FullName { get; init; }
    public bool? IsActive { get; init; }
}
