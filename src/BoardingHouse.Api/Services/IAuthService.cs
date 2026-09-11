using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;

namespace BoardingHouse.Api.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<(UserResponse User, string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt)> LoginAsync(
        LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt)> RefreshTokenAsync(
        string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
}
