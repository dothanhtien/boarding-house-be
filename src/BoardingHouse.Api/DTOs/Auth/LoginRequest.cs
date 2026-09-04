namespace BoardingHouse.Api.DTOs.Auth;

public record LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}
