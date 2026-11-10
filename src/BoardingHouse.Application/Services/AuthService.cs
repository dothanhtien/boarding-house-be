using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

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
        var email = EmailAddress.Normalize(request.Email)!;
        var phone = PhoneNumber.Normalize(request.Phone);
        var maskedEmail = EmailAddress.Mask(email);
        var maskedPhone = PhoneNumber.Mask(phone ?? "");

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, phone, cancellationToken))
        {
            logger.LogWarning("Registration failed: email or phone already in use ({Email}, {Phone})", maskedEmail, maskedPhone);
            throw new AppConflictException("Email or phone already in use");
        }

        var user = User.Register(email, phone, PasswordHasher.Hash(request.Password), request.FullName, SentinelActors.SelfRegistration);

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

    public async Task<(UserResponse User, AuthTokens Tokens)> LoginAsync(
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
            || !PasswordHasher.Verify(request.Password, user.PasswordHash)
            || !user.IsActive)
        {
            logger.LogWarning("Login failed for {Identifier} from {IpAddress}", isEmail ? EmailAddress.Mask(identifier) : PhoneNumber.Mask(identifier), ipAddress);
            throw new AppUnauthorizedException("Email/phone or password is invalid");
        }

        user.RecordLogin(DateTimeOffset.UtcNow);

        var tokens = await IssueTokensAsync(user, ipAddress, userAgent, cancellationToken);

        logger.LogInformation("User {UserId} logged in from {IpAddress}", user.Id, ipAddress);

        var response = user.Adapt<UserResponse>() with
        {
            AvatarUrl = userWithAvatar!.Media is { } avatar ? storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType) : null
        };

        return (response, tokens);
    }

    public async Task<AuthTokens> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var tokenHash = tokenService.HashToken(refreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null)
        {
            logger.LogWarning("Refresh token failed: token not found ({IpAddress})", ipAddress);
            throw new AppUnauthorizedException("Invalid refresh token");
        }

        if (existingToken.IsRevoked)
        {
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; revoking active token chain ({IpAddress})",
                existingToken.UserId, ipAddress);

            var activeTokens = await refreshTokenRepository.GetActiveByUserIdAsync(existingToken.UserId, cancellationToken);
            foreach (var activeToken in activeTokens)
            {
                activeToken.Revoke(RevokedReason.Suspicious, now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AppUnauthorizedException("Refresh token has been revoked; please log in again");
        }

        if (existingToken.IsExpiredAt(now))
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
        var newExpiresAt = now.Add(tokenService.RefreshTokenLifetime);
        var newRefreshTokenEntity = existingToken.RotateTo(tokenService.HashToken(newRefreshToken), newExpiresAt, ipAddress, userAgent, now);

        await refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Refresh token rotated for user {UserId}", user.Id);

        var (accessToken, accessTokenExpiresAt) = tokenService.GenerateAccessToken(user);

        return new AuthTokens(accessToken, accessTokenExpiresAt, newRefreshToken, newExpiresAt);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenService.HashToken(refreshToken);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || existingToken.IsRevoked)
        {
            return;
        }

        existingToken.Revoke(RevokedReason.Logout, DateTimeOffset.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} logged out", existingToken.UserId);
    }

    private async Task<AuthTokens> IssueTokensAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var (accessToken, accessTokenExpiresAt) = tokenService.GenerateAccessToken(user);
        var now = DateTimeOffset.UtcNow;
        var refreshToken = tokenService.GenerateRefreshToken();
        var expiresAt = now.Add(tokenService.RefreshTokenLifetime);

        var refreshTokenEntity = RefreshToken.Issue(user.Id, tokenService.HashToken(refreshToken), expiresAt, ipAddress, userAgent, now);

        await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokens(accessToken, accessTokenExpiresAt, refreshToken, expiresAt);
    }
}
