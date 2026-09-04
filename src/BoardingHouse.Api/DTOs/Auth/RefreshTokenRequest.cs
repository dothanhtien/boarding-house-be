namespace BoardingHouse.Api.DTOs.Auth;

public record RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}
