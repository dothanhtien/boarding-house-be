using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardingHouse.UnitTests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly AppDbContext _context;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            })
            .Build();

        _authService = new AuthService(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _tokenService.Object,
            _context,
            configuration,
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task RegisterAsync_EmailAlreadyExists_ThrowsConflictAppException()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync("taken@test.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new RegisterRequest
        {
            Email = "taken@test.com",
            Password = "password",
            PasswordConfirmation = "password",
            FullName = "Test User"
        };

        await Assert.ThrowsAsync<ConflictAppException>(() => _authService.RegisterAsync(request));
    }

    [Fact]
    public async Task RegisterAsync_NewEmail_ReturnsUserResponse_AndHashesPassword()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new RegisterRequest
        {
            Email = "new@test.com",
            Password = "password",
            PasswordConfirmation = "password",
            FullName = "Test User"
        };

        var response = await _authService.RegisterAsync(request);

        Assert.Equal("new@test.com", response.Email);
        Assert.Equal("Test User", response.FullName);

        _userRepository.Verify(r => r.AddAsync(
            It.Is<User>(u => u.Email == "new@test.com" && u.PasswordHash != "password" && u.PasswordHash != string.Empty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedAppException()
    {
        var user = new User
        {
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password"),
            FullName = "Test User",
            CreatedBy = SentinelActors.System
        };
        _userRepository.Setup(r => r.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new LoginRequest { Email = "user@test.com", Password = "wrong-password" };

        await Assert.ThrowsAsync<UnauthorizedAppException>(() => _authService.LoginAsync(request, null, null));
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsUnauthorizedAppException()
    {
        var user = new User
        {
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password1"),
            FullName = "Test User",
            IsActive = false,
            CreatedBy = SentinelActors.System
        };
        _userRepository.Setup(r => r.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new LoginRequest { Email = "user@test.com", Password = "password1" };

        await Assert.ThrowsAsync<UnauthorizedAppException>(() => _authService.LoginAsync(request, null, null));
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens_AndUpdatesLastLoginAt()
    {
        var user = new User { Email = "user@test.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("password1"), FullName = "Test User", CreatedBy = SentinelActors.System };
        _userRepository.Setup(r => r.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");
        _tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns("refresh-token-hash");

        var request = new LoginRequest { Email = "user@test.com", Password = "password1" };

        var response = await _authService.LoginAsync(request, "127.0.0.1", "test-agent");

        Assert.Equal("access-token", response.AccessToken);
        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_TokenNotFound_ThrowsUnauthorizedAppException()
    {
        _tokenService.Setup(t => t.HashToken("unknown")).Returns("unknown-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("unknown-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() => _authService.RefreshTokenAsync("unknown", null, null));
    }

    [Fact]
    public async Task RefreshTokenAsync_AlreadyRevoked_ThrowsUnauthorizedAppException_AndRevokesActiveChain()
    {
        var userId = Guid.NewGuid();
        var revokedToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = "revoked-hash",
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        var otherActiveToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = "other-active-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        _tokenService.Setup(t => t.HashToken("stolen-token")).Returns("revoked-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("revoked-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedToken);
        _refreshTokenRepository
            .Setup(r => r.GetActiveByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([otherActiveToken]);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() => _authService.RefreshTokenAsync("stolen-token", null, null));

        Assert.NotNull(otherActiveToken.RevokedAt);
        Assert.Equal(RevokedReason.Suspicious, otherActiveToken.RevokedReason);
    }

    [Fact]
    public async Task RefreshTokenAsync_Expired_ThrowsUnauthorizedAppException()
    {
        var token = new RefreshToken
        {
            UserId = Guid.NewGuid(),
            TokenHash = "expired-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _tokenService.Setup(t => t.HashToken("expired-token")).Returns("expired-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("expired-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() => _authService.RefreshTokenAsync("expired-token", null, null));
    }

    [Fact]
    public async Task RefreshTokenAsync_Valid_RevokesOldToken_AndReturnsNewTokens()
    {
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        var oldToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "old-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        _tokenService.Setup(t => t.HashToken("old-token")).Returns("old-hash");
        _tokenService.Setup(t => t.HashToken("new-token")).Returns("new-hash");
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("new-token");
        _tokenService.Setup(t => t.GenerateAccessToken(user)).Returns("new-access-token");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldToken);
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var response = await _authService.RefreshTokenAsync("old-token", null, null);

        Assert.Equal("new-access-token", response.AccessToken);
        Assert.Equal("new-token", response.RefreshToken);
        Assert.NotNull(oldToken.RevokedAt);
        Assert.Equal(RevokedReason.Rotation, oldToken.RevokedReason);
        _refreshTokenRepository.Verify(r => r.AddAsync(
            It.Is<RefreshToken>(t => t.TokenHash == "new-hash"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_RevokesToken_ByHash()
    {
        var token = new RefreshToken { UserId = Guid.NewGuid(), TokenHash = "logout-hash" };
        _tokenService.Setup(t => t.HashToken("logout-token")).Returns("logout-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("logout-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        await _authService.LogoutAsync("logout-token");

        Assert.NotNull(token.RevokedAt);
        Assert.Equal(RevokedReason.Logout, token.RevokedReason);
    }

    [Fact]
    public async Task LogoutAsync_TokenNotFound_DoesNotThrow()
    {
        _tokenService.Setup(t => t.HashToken("unknown")).Returns("unknown-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("unknown-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await _authService.LogoutAsync("unknown");
    }
}
