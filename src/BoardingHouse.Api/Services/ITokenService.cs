using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string HashToken(string token);
}
