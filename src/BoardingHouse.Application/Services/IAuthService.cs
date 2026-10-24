using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Users;

namespace BoardingHouse.Application.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(UserResponse User, AuthTokens Tokens)> LoginAsync(
        LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AuthTokens> RefreshTokenAsync(
        string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
}
