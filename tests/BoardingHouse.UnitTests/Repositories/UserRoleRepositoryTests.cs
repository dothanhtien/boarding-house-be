using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests.Repositories;

public class UserRoleRepositoryTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static Permission NewPermission(string resource, string action) => new()
    {
        Resource = resource,
        Action = action,
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public async Task GetPermissionsByUserIdAsync_ActiveRole_ReturnsDistinctPermissions()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var role = new Role { Name = "Admin", Slug = "admin", IsActive = true, CreatedBy = SentinelActors.System };
        var usersRead = NewPermission("users", "read");
        var usersWrite = NewPermission("users", "write");

        context.Roles.Add(role);
        context.Permissions.AddRange(usersRead, usersWrite);
        context.RolePermissions.AddRange(
            new RolePermission { Role = role, Permission = usersRead, CreatedBy = SentinelActors.System },
            new RolePermission { Role = role, Permission = usersWrite, CreatedBy = SentinelActors.System });
        context.UserRoles.Add(new UserRole { UserId = userId, Role = role, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetPermissionsByUserIdAsync(userId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Resource == "users" && p.Action == "read");
        Assert.Contains(result, p => p.Resource == "users" && p.Action == "write");
    }

    [Fact]
    public async Task GetPermissionsByUserIdAsync_InactiveRole_ReturnsEmpty()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var role = new Role { Name = "Disabled", Slug = "disabled", IsActive = false, CreatedBy = SentinelActors.System };
        var permission = NewPermission("users", "read");

        context.Roles.Add(role);
        context.Permissions.Add(permission);
        context.RolePermissions.Add(new RolePermission { Role = role, Permission = permission, CreatedBy = SentinelActors.System });
        context.UserRoles.Add(new UserRole { UserId = userId, Role = role, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetPermissionsByUserIdAsync(userId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPermissionsByUserIdAsync_UserWithNoRoles_ReturnsEmpty()
    {
        using var context = CreateContext();
        var repository = new UserRoleRepository(context);

        var result = await repository.GetPermissionsByUserIdAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPermissionsByUserIdAsync_SharedPermissionAcrossRoles_ReturnsDistinctEntry()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var roleA = new Role { Name = "Role A", Slug = "role-a", IsActive = true, CreatedBy = SentinelActors.System };
        var roleB = new Role { Name = "Role B", Slug = "role-b", IsActive = true, CreatedBy = SentinelActors.System };
        var sharedPermission = NewPermission("users", "read");

        context.Roles.AddRange(roleA, roleB);
        context.Permissions.Add(sharedPermission);
        context.RolePermissions.AddRange(
            new RolePermission { Role = roleA, Permission = sharedPermission, CreatedBy = SentinelActors.System },
            new RolePermission { Role = roleB, Permission = sharedPermission, CreatedBy = SentinelActors.System });
        context.UserRoles.AddRange(
            new UserRole { UserId = userId, Role = roleA, CreatedBy = SentinelActors.System },
            new UserRole { UserId = userId, Role = roleB, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetPermissionsByUserIdAsync(userId);

        Assert.Single(result);
        Assert.Equal(("users", "read"), result[0]);
    }

    [Fact]
    public async Task AddAsync_NewUserRole_PersistsToDatabase()
    {
        using var context = CreateContext();
        var role = new Role { Name = "Admin", Slug = "admin", CreatedBy = SentinelActors.System };
        context.Roles.Add(role);
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var userRole = new UserRole { UserId = Guid.NewGuid(), RoleId = role.Id, CreatedBy = SentinelActors.System };

        await repository.AddAsync(userRole);
        await context.SaveChangesAsync();

        var result = await context.UserRoles.FirstOrDefaultAsync(ur => ur.Id == userRole.Id);
        Assert.NotNull(result);
    }
}
