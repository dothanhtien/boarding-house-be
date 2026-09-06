using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Repositories;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Repositories;

public class UserRoleRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(string email = "user@test.com") =>
        new() { Email = email, PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };

    private static Role NewRole(string name, bool isActive = true) =>
        new() { Slug = name.ToLowerInvariant(), Name = name, IsActive = isActive, CreatedBy = SentinelActors.System };

    private static Permission NewPermission(string resource, string action) =>
        new() { Resource = resource, Action = action, CreatedBy = SentinelActors.System };

    [Fact]
    public async Task GetPermissionsByUserIdAsync_UserWithRole_ReturnsPermissionsViaRealJoin()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        var role = NewRole("PLATFORM_STAFF");
        var readUser = NewPermission("user", "read");
        var readRole = NewPermission("role", "read");
        context.AddRange(user, role, readUser, readRole);
        await context.SaveChangesAsync();

        context.RolePermissions.AddRange(
            new RolePermission { RoleId = role.Id, PermissionId = readUser.Id, CreatedBy = SentinelActors.System },
            new RolePermission { RoleId = role.Id, PermissionId = readRole.Id, CreatedBy = SentinelActors.System });
        context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetPermissionsByUserIdAsync(user.Id);

        Assert.Equal(2, result.Count);
        Assert.Contains(("user", "read"), result);
        Assert.Contains(("role", "read"), result);
    }

    [Fact]
    public async Task GetPermissionsByUserIdAsync_RoleInactive_ReturnsEmpty()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        var role = NewRole("PLATFORM_STAFF", isActive: false);
        var permission = NewPermission("user", "read");
        context.AddRange(user, role, permission);
        await context.SaveChangesAsync();

        context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, CreatedBy = SentinelActors.System });
        context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetPermissionsByUserIdAsync(user.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPermissionsByUserIdAsync_UserRoleSoftDeleted_ReturnsEmpty()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        var role = NewRole("PLATFORM_STAFF");
        var permission = NewPermission("user", "read");
        context.AddRange(user, role, permission);
        await context.SaveChangesAsync();

        context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, CreatedBy = SentinelActors.System });
        var userRole = new UserRole { UserId = user.Id, RoleId = role.Id, CreatedBy = SentinelActors.System };
        context.UserRoles.Add(userRole);
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        repository.SoftDelete(userRole);
        await context.SaveChangesAsync();

        var result = await repository.GetPermissionsByUserIdAsync(user.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPermissionsByUserIdAsync_UserWithoutRole_ReturnsEmpty()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetPermissionsByUserIdAsync(user.Id);

        Assert.Empty(result);
    }
}
