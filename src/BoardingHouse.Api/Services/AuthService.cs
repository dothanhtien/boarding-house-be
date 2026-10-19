using Mapster;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Services;

namespace BoardingHouse.Api.Services;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    ITokenService tokenService,
    IStorageProvider storageProvider,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = PhoneNumber.Normalize(request.Phone);
        var maskedEmail = EmailAddress.Mask(email);
        var maskedPhone = PhoneNumber.Mask(phone ?? "");

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, phone, cancellationToken))
        {
            logger.LogWarning("Registration failed: email or phone already in use ({Email}, {Phone})", maskedEmail, maskedPhone);
            throw new AppConflictException("Email or phone already in use");
        }

        var user = new User
        {
            Email = email,
            Phone = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            FullName = request.FullName,
            CreatedBy = SentinelActors.SelfRegistration
        };

        await userRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User registered ({UserId}, {Email}, {Phone})", user.Id, maskedEmail, maskedPhone);

        return user.Adapt<UserResponse>();
    }

    public async Task<UserResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (user, avatar) = await userRepository.GetByIdWithRolesAndOrganizationsAsync(userId, cancellationToken)
            ?? throw new AppUnauthorizedException("Current user is not set on an authenticated request");

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = avatar is null ? null : storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType)
        };
    }

    public async Task<(UserResponse User, string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt)> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var isEmail = LoginRequest.IsEmail(request.Identifier);
        var identifier = isEmail ? request.Identifier.Trim() : PhoneNumber.Normalize(request.Identifier)!;
        var userWithAvatar = isEmail
            ? await userRepository.GetByEmailWithRolesAndOrganizationsAsync(identifier, cancellationToken)
            : await userRepository.GetByPhoneWithRolesAndOrganizationsAsync(identifier, cancellationToken);
        var user = userWithAvatar?.Entity;

        if (user is null
            || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)
            || !user.IsActive)
        {
            logger.LogWarning("Login failed for {Identifier} from {IpAddress}", isEmail ? EmailAddress.Mask(identifier) : PhoneNumber.Mask(identifier), ipAddress);
            throw new AppUnauthorizedException("Email/phone or password is invalid");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        var (accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt) = await IssueTokensAsync(user, ipAddress, userAgent, cancellationToken);

        logger.LogInformation("User {UserId} logged in from {IpAddress}", user.Id, ipAddress);

        var response = user.Adapt<UserResponse>() with
        {
            AvatarUrl = userWithAvatar!.Media is { } avatar ? storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType) : null
        };

        return (response, accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt);
    }

    public async Task<(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt)> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(refreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null)
        {
            logger.LogWarning("Refresh token failed: token not found ({IpAddress})", ipAddress);
            throw new AppUnauthorizedException("Invalid refresh token");
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

            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AppUnauthorizedException("Refresh token has been revoked; please log in again");
        }

        if (existingToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            logger.LogWarning("Refresh token failed: token expired for user {UserId}", existingToken.UserId);
            throw new AppUnauthorizedException("Refresh token has expired");
        }

        var user = await userRepository.GetByIdAsync(existingToken.UserId, cancellationToken)
            ?? throw new AppUnauthorizedException("Invalid refresh token");

        if (!user.IsActive)
        {
            logger.LogWarning("Refresh token failed: user {UserId} is inactive", user.Id);
            throw new AppUnauthorizedException("Invalid refresh token");
        }

        var newRefreshToken = tokenService.GenerateRefreshToken();
        var newExpiresAt = DateTimeOffset.UtcNow.Add(tokenService.RefreshTokenLifetime);
        var newRefreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(newRefreshToken),
            ExpiresAt = newExpiresAt,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        existingToken.RevokedAt = DateTimeOffset.UtcNow;
        existingToken.RevokedReason = RevokedReason.Rotation;
        existingToken.ReplacedById = newRefreshTokenEntity.Id;

        await refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Refresh token rotated for user {UserId}", user.Id);

        var (accessToken, accessTokenExpiresAt) = tokenService.GenerateAccessToken(user);

        return (accessToken, accessTokenExpiresAt, newRefreshToken, newExpiresAt);
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} logged out", existingToken.UserId);
    }

    private async Task<(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt)> IssueTokensAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var (accessToken, accessTokenExpiresAt) = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(tokenService.RefreshTokenLifetime);

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(refreshToken),
            ExpiresAt = expiresAt,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (accessToken, accessTokenExpiresAt, refreshToken, expiresAt);
    }
}
