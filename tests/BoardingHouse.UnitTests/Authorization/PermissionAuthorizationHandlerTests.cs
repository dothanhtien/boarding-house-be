using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Moq;

namespace BoardingHouse.UnitTests.Authorization;

public class PermissionAuthorizationHandlerTests
{
    private readonly Mock<IPermissionService> _permissionService = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();

    private PermissionAuthorizationHandler CreateSut() =>
        new(_permissionService.Object, _currentUserAccessor.Object);

    private static AuthorizationHandlerContext CreateContext(PermissionRequirement requirement) =>
        new([requirement], new System.Security.Claims.ClaimsPrincipal(), resource: null);

    [Fact]
    public async Task HandleAsync_NoCurrentUser_DoesNotSucceed()
    {
        _currentUserAccessor.SetupGet(a => a.User).Returns((User?)null);
        var requirement = new PermissionRequirement("user", "read");
        var context = CreateContext(requirement);

        await CreateSut().HandleAsync(context);

        Assert.False(context.HasSucceeded);
        _permissionService.Verify(
            s => s.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UserHasPermission_Succeeds()
    {
        var user = new User { Email = "user@test.com", PasswordHash = "hash", FullName = "Test User", CreatedBy = SentinelActors.System };
        _currentUserAccessor.SetupGet(a => a.User).Returns(user);
        _permissionService
            .Setup(s => s.HasPermissionAsync(user.Id, "user", "read", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var requirement = new PermissionRequirement("user", "read");
        var context = CreateContext(requirement);

        await CreateSut().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_UserLacksPermission_DoesNotSucceed()
    {
        var user = new User { Email = "user@test.com", PasswordHash = "hash", FullName = "Test User", CreatedBy = SentinelActors.System };
        _currentUserAccessor.SetupGet(a => a.User).Returns(user);
        _permissionService
            .Setup(s => s.HasPermissionAsync(user.Id, "user", "delete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var requirement = new PermissionRequirement("user", "delete");
        var context = CreateContext(requirement);

        await CreateSut().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
