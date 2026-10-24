using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.UnitTests.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Services.Storage;

namespace BoardingHouse.UnitTests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        MapsterTestSupport.EnsureUserMappingRegistered();

        _tokenService.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(7));

        _authService = new AuthService(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _unitOfWork.Object,
            _tokenService.Object,
            new Mock<IStorageProvider>().Object,
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task RegisterAsync_EmailAlreadyExists_ThrowsAppConflictException()
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

        await Assert.ThrowsAsync<AppConflictException>(() => _authService.RegisterAsync(request));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task GetCurrentUserAsync_UserExists_ReturnsUserResponse_WithPlatformRoleAndOrganizations()
    {
        var user = new User { Email = "user@test.com", PasswordHash = "hash", FullName = "Test User", CreatedBy = SentinelActors.System };
        var platformAdminRole = new Role { Slug = RoleSlugs.PlatformAdmin, Name = "Platform Admin", Scope = RoleScope.Platform, CreatedBy = SentinelActors.System };
        var organizationRole = new Role { Slug = RoleSlugs.OrganizationAdmin, Name = "Organization Admin", Scope = RoleScope.Organization, CreatedBy = SentinelActors.System };
        var organization = new Organization { Name = "Test Org", CreatedBy = SentinelActors.System };
        user.UserRoles = [new UserRole { UserId = user.Id, User = user, RoleId = platformAdminRole.Id, Role = platformAdminRole, CreatedBy = SentinelActors.System }];
        user.OrganizationMembers =
        [
            new OrganizationMember
            {
                UserId = user.Id, User = user,
                OrganizationId = organization.Id, Organization = organization,
                RoleId = organizationRole.Id, Role = organizationRole,
                CreatedBy = SentinelActors.System
            }
        ];

        _userRepository.Setup(r => r.GetByIdWithRolesAndOrganizationsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var response = await _authService.GetCurrentUserAsync(user.Id);

        Assert.Equal(user.Id, response.Id);
        Assert.Equal(RoleSlugs.PlatformAdmin, response.PlatformRole?.Slug);
        var organizationEntry = Assert.Single(response.Organizations);
        Assert.Equal(organization.Id, organizationEntry.OrganizationId);
        Assert.Equal(RoleSlugs.OrganizationAdmin, organizationEntry.RoleSlug);
    }

    [Fact]
    public async Task GetCurrentUserAsync_UserNotFound_ThrowsAppUnauthorizedException()
    {
        _userRepository.Setup(r => r.GetByIdWithRolesAndOrganizationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EntityWithMedia<User>?)null);

        await Assert.ThrowsAsync<AppUnauthorizedException>(() => _authService.GetCurrentUserAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsAppUnauthorizedException()
    {
        var user = new User
        {
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password"),
            FullName = "Test User",
            CreatedBy = SentinelActors.System
        };
        _userRepository.Setup(r => r.GetByEmailWithRolesAndOrganizationsAsync("user@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var request = new LoginRequest { Identifier = "user@test.com", Password = "wrong-password" };

        await Assert.ThrowsAsync<AppUnauthorizedException>(() => _authService.LoginAsync(request, null, null));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsAppUnauthorizedException()
    {
        var user = new User
        {
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password1"),
            FullName = "Test User",
            IsActive = false,
            CreatedBy = SentinelActors.System
        };
        _userRepository.Setup(r => r.GetByEmailWithRolesAndOrganizationsAsync("user@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var request = new LoginRequest { Identifier = "user@test.com", Password = "password1" };

        await Assert.ThrowsAsync<AppUnauthorizedException>(() => _authService.LoginAsync(request, null, null));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens_AndUpdatesLastLoginAt()
    {
        var user = new User { Email = "user@test.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("password1"), FullName = "Test User", CreatedBy = SentinelActors.System };
        var accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        _userRepository.Setup(r => r.GetByEmailWithRolesAndOrganizationsAsync("user@test.com", It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        _tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns(("access-token", accessTokenExpiresAt));
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");
        _tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns("refresh-token-hash");

        var request = new LoginRequest { Identifier = "user@test.com", Password = "password1" };

        var beforeCall = DateTimeOffset.UtcNow;
        var response = await _authService.LoginAsync(request, "127.0.0.1", "test-agent");

        Assert.Equal("user@test.com", response.User.Email);
        Assert.Equal("access-token", response.Tokens.AccessToken);
        Assert.Equal(accessTokenExpiresAt, response.Tokens.AccessTokenExpiresAt);
        Assert.Equal("refresh-token", response.Tokens.RefreshToken);
        Assert.InRange(response.Tokens.RefreshTokenExpiresAt, beforeCall.AddDays(7).AddSeconds(-5), beforeCall.AddDays(7).AddSeconds(5));
        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_TokenNotFound_ThrowsAppUnauthorizedException()
    {
        _tokenService.Setup(t => t.HashToken("unknown")).Returns("unknown-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("unknown-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await Assert.ThrowsAsync<AppUnauthorizedException>(() => _authService.RefreshTokenAsync("unknown", null, null));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshTokenAsync_AlreadyRevoked_ThrowsAppUnauthorizedException_AndRevokesActiveChain()
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

        await Assert.ThrowsAsync<AppUnauthorizedException>(() => _authService.RefreshTokenAsync("stolen-token", null, null));

        Assert.NotNull(otherActiveToken.RevokedAt);
        Assert.Equal(RevokedReason.Suspicious, otherActiveToken.RevokedReason);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_Expired_ThrowsAppUnauthorizedException()
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

        await Assert.ThrowsAsync<AppUnauthorizedException>(() => _authService.RefreshTokenAsync("expired-token", null, null));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        var accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        _tokenService.Setup(t => t.HashToken("old-token")).Returns("old-hash");
        _tokenService.Setup(t => t.HashToken("new-token")).Returns("new-hash");
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("new-token");
        _tokenService.Setup(t => t.GenerateAccessToken(user)).Returns(("new-access-token", accessTokenExpiresAt));
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldToken);
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var beforeCall = DateTimeOffset.UtcNow;
        var response = await _authService.RefreshTokenAsync("old-token", null, null);

        Assert.Equal("new-access-token", response.AccessToken);
        Assert.Equal(accessTokenExpiresAt, response.AccessTokenExpiresAt);
        Assert.Equal("new-token", response.RefreshToken);
        Assert.InRange(response.RefreshTokenExpiresAt, beforeCall.AddDays(7).AddSeconds(-5), beforeCall.AddDays(7).AddSeconds(5));
        Assert.NotNull(oldToken.RevokedAt);
        Assert.Equal(RevokedReason.Rotation, oldToken.RevokedReason);
        _refreshTokenRepository.Verify(r => r.AddAsync(
            It.Is<RefreshToken>(t => t.TokenHash == "new-hash"),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_TokenNotFound_DoesNotThrow()
    {
        _tokenService.Setup(t => t.HashToken("unknown")).Returns("unknown-hash");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("unknown-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        await _authService.LogoutAsync("unknown");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_ByPhone_NormalizesAndLooksUpByPhone()
    {
        var user = new User { Phone = "0912345678", PasswordHash = BCrypt.Net.BCrypt.HashPassword("password1"), FullName = "Tenant", CreatedBy = SentinelActors.System };
        _userRepository.Setup(r => r.GetByPhoneWithRolesAndOrganizationsAsync("0912345678", It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        _tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns(("access-token", DateTimeOffset.UtcNow.AddMinutes(15)));
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");
        _tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns("refresh-token-hash");

        var response = await _authService.LoginAsync(new LoginRequest { Identifier = "+84 912.345.678", Password = "password1" }, null, null);

        Assert.Null(response.User.Email);
        Assert.Equal("0912345678", response.User.Phone);
        _userRepository.Verify(r => r.GetByEmailWithRolesAndOrganizationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_ByPhoneUnknown_ThrowsAppUnauthorizedException()
    {
        _userRepository.Setup(r => r.GetByPhoneWithRolesAndOrganizationsAsync("0912345678", It.IsAny<CancellationToken>())).ReturnsAsync((EntityWithMedia<User>?)null);

        var ex = await Assert.ThrowsAsync<AppUnauthorizedException>(() =>
            _authService.LoginAsync(new LoginRequest { Identifier = "0912345678", Password = "password1" }, null, null));

        Assert.Equal("Email/phone or password is invalid", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_InternationalVietnamPhone_StoresNormalizedPhone()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await _authService.RegisterAsync(new RegisterRequest
        {
            Email = "user@test.com",
            Phone = "+84912345678",
            Password = "password",
            PasswordConfirmation = "password",
            FullName = "Test User"
        });

        Assert.Equal("0912345678", response.Phone);
        _userRepository.Verify(r => r.ExistsByEmailOrPhoneAsync("user@test.com", "0912345678", It.IsAny<CancellationToken>()), Times.Once);
    }
}
