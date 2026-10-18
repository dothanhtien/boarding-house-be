namespace BoardingHouse.Api.DTOs.Auth;

public record LoginRequest
{
    public required string Identifier { get; init; }
    public required string Password { get; init; }

    public static bool IsEmail(string identifier) => identifier.Contains('@');
}
