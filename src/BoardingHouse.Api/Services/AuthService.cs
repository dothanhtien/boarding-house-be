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
    IConfiguration configuration,
    ILogger<AuthService> logger) : IAuthService
{
    private TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays"));

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, request.Phone, cancellationToken))
        {
            logger.LogWarning("Registration failed: email or phone already in use ({Email})", email);
            throw new ConflictAppException("Email or phone already in use");
        }

        var user = new User
        {
            Email = email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            FullName = request.FullName,
            CreatedBy = SentinelActors.SelfRegistration
        };

        await userRepository.AddAsync(user, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User registered ({UserId}, {Email})", user.Id, email);

        return user.Adapt<UserResponse>();
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null
            || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)
            || !user.IsActive)
        {
            logger.LogWarning("Login failed for {Email} from {IpAddress}", request.Email, ipAddress);
            throw new UnauthorizedAppException("Email or password is invalid");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        var response = await IssueTokensAsync(user, ipAddress, userAgent, cancellationToken);

        logger.LogInformation("User {UserId} logged in from {IpAddress}", user.Id, ipAddress);

        return response;
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(refreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null)
        {
            logger.LogWarning("Refresh token failed: token not found ({IpAddress})", ipAddress);
            throw new UnauthorizedAppException("Invalid refresh token");
        }

        if (existingToken.RevokedAt is not null)
        {
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; revoking active token chain ({IpAddress})",
                existingToken.UserId, ipAddress);

            var activeTokens = await refreshTokenRepository.GetActiveByUserIdAsync(existingToken.UserId, cancellationToken);
            foreach (var activeToken in activeTokens)
            {
                activeToken.RevokedAt = DateTimeOffset.UtcNow;
                activeToken.RevokedReason = RevokedReason.Suspicious;
            }

            await context.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedAppException("Refresh token has been revoked; please log in again");
        }

        if (existingToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            logger.LogWarning("Refresh token failed: token expired for user {UserId}", existingToken.UserId);
            throw new UnauthorizedAppException("Refresh token has expired");
        }

        var user = await userRepository.GetByIdAsync(existingToken.UserId, cancellationToken)
            ?? throw new UnauthorizedAppException("Invalid refresh token");

        if (!user.IsActive)
        {
            logger.LogWarning("Refresh token failed: user {UserId} is inactive", user.Id);
            throw new UnauthorizedAppException("Invalid refresh token");
        }

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

        await refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Refresh token rotated for user {UserId}", user.Id);

        return new AuthResponse
        {
            AccessToken = tokenService.GenerateAccessToken(user),
            RefreshToken = newRefreshToken
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

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} logged out", existingToken.UserId);
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
            RefreshToken = refreshToken
        };
    }
}
