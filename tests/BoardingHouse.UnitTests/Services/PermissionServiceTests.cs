using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Caching;
using Moq;

namespace BoardingHouse.UnitTests.Services;

public class PermissionServiceTests
{
    private readonly Mock<IUserRoleRepository> _userRoleRepository = new();
    private readonly Mock<IRoleRepository> _roleRepository = new();
    private readonly Mock<IRolePermissionCache> _rolePermissionCache = new();

    private PermissionService CreateSut() =>
        new(_userRoleRepository.Object, _roleRepository.Object, _rolePermissionCache.Object);

    [Fact]
    public async Task HasPermissionAsync_UserWithNoRoles_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        _userRoleRepository.Setup(r => r.GetActiveRoleIdsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().HasPermissionAsync(userId, "user", "read");
        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_RolePermissionCacheMiss_FallsBackToDatabaseAndCaches()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        _userRoleRepository.Setup(r => r.GetActiveRoleIdsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([roleId]);
        _rolePermissionCache.Setup(c => c.GetAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<(string, string)>?)null);
        _roleRepository.Setup(r => r.GetPermissionsByRoleIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([("user", "read")]);

        var result = await CreateSut().HasPermissionAsync(userId, "user", "read");

        Assert.True(result);
        _rolePermissionCache.Verify(c =>
            c.SetAsync(
                roleId,
                It.Is<List<(string, string)>>(p => p.Any(x => x.Item1 == "user" && x.Item2 == "read")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HasPermissionAsync_RolePermissionCacheHit_SkipsDatabase()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _userRoleRepository.Setup(r => r.GetActiveRoleIdsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([roleId]);
        _rolePermissionCache.Setup(c => c.GetAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([("user", "read")]);

        var result = await CreateSut().HasPermissionAsync(userId, "user", "read");

        Assert.True(result);
        _roleRepository.Verify(r => r.GetPermissionsByRoleIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HasPermissionAsync_MissingPermission_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _userRoleRepository.Setup(r => r.GetActiveRoleIdsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([roleId]);
        _rolePermissionCache.Setup(c => c.GetAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([("user", "read")]);

        var result = await CreateSut().HasPermissionAsync(userId, "user", "write");

        Assert.False(result);
    }
}
