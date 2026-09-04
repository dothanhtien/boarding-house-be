namespace BoardingHouse.Api.DTOs.Auth;

public record RegisterRequest
{
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public required string FullName { get; init; }
    public required string Password { get; init; }
    public required string PasswordConfirmation { get; init; }
}
