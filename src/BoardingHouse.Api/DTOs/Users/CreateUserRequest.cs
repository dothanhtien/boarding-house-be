namespace BoardingHouse.Api.DTOs.Users;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public required string FullName { get; init; }
    public required string Password { get; init; }
    public required string PasswordConfirmation { get; init; }
}
