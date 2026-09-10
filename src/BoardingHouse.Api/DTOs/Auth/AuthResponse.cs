namespace BoardingHouse.Api.DTOs.Auth;

public record AuthResponse
{
    public required string AccessToken { get; init; }
}
