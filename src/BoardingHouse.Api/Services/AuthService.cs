using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;

namespace BoardingHouse.Api.Services;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    AppDbContext context,
    IConfiguration configuration) : IAuthService
{
    private TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays"));

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (await userRepository.ExistsByEmailOrPhoneAsync(request.Email, request.Phone, cancellationToken))
        {
            throw new ConflictAppException("Email or phone already in use");
        }

        var user = new User
        {
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            FullName = request.FullName,
            CreatedBy = SystemActor.Id
        };

        await userRepository.AddAsync(user, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserResponse>();
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null
            || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)
            || !user.IsActive)
        {
            throw new UnauthorizedAppException("Email or password is invalid");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        return await IssueTokensAsync(user, ipAddress, userAgent, cancellationToken);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(refreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken)
            ?? throw new UnauthorizedAppException("Invalid refresh token");

        if (existingToken.RevokedAt is not null)
        {
            var activeTokens = await refreshTokenRepository.GetActiveByUserIdAsync(existingToken.UserId, cancellationToken);
            foreach (var activeToken in activeTokens)
            {
                activeToken.RevokedAt = DateTimeOffset.UtcNow;
                activeToken.RevokedReason = RevokedReason.Suspicious;
                refreshTokenRepository.Update(activeToken);
            }

            await context.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedAppException("Refresh token has been revoked; please log in again");
        }

        if (existingToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAppException("Refresh token has expired");
        }

        var user = await userRepository.GetByIdAsync(existingToken.UserId, cancellationToken)
            ?? throw new UnauthorizedAppException("Invalid refresh token");

        var newRefreshToken = tokenService.GenerateRefreshToken();
        var newRefreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(newRefreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        existingToken.RevokedAt = DateTimeOffset.UtcNow;
        existingToken.RevokedReason = RevokedReason.Rotation;
        existingToken.ReplacedById = newRefreshTokenEntity.Id;
        refreshTokenRepository.Update(existingToken);

        await refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = tokenService.GenerateAccessToken(user),
            RefreshToken = newRefreshToken,
            ExpiresAt = newRefreshTokenEntity.ExpiresAt
        };
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(refreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || existingToken.RevokedAt is not null)
        {
            return;
        }

        existingToken.RevokedAt = DateTimeOffset.UtcNow;
        existingToken.RevokedReason = RevokedReason.Logout;
        refreshTokenRepository.Update(existingToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(refreshToken),
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = refreshTokenEntity.ExpiresAt
        };
    }
}
