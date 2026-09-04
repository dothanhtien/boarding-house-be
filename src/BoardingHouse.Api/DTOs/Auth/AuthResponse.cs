namespace BoardingHouse.Api.DTOs.Auth;

public record AuthResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
}
