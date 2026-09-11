using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;

namespace BoardingHouse.Api.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<(AuthResponse Response, string RefreshToken, DateTimeOffset ExpiresAt)> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);
    Task<(AuthResponse Response, string RefreshToken, DateTimeOffset ExpiresAt)> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
}
