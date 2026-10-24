namespace BoardingHouse.Application.DTOs.Auth;

public record LoginRequest
{
    public string Identifier { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;

    public static bool IsEmail(string identifier) => identifier.Contains('@');
}
