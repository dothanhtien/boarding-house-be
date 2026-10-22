using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services;

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }
    (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string HashToken(string token);
}
