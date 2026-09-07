using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using BoardingHouse.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardingHouse.UnitTests.Repositories;

public class UserRoleRepositoryTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                new CurrentUserAccessor(),
                NullLogger<AuditableEntitySaveChangesInterceptor>.Instance))
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetActiveRoleIdsByUserIdAsync_ActiveRole_ReturnsRoleId()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var role = new Role { Name = "Admin", Slug = "admin", IsActive = true, CreatedBy = SentinelActors.System };
        context.Roles.Add(role);
        context.UserRoles.Add(new UserRole { UserId = userId, Role = role, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetActiveRoleIdsByUserIdAsync(userId);

        Assert.Equal([role.Id], result);
    }

    [Fact]
    public async Task GetActiveRoleIdsByUserIdAsync_InactiveRole_ReturnsEmpty()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var role = new Role { Name = "Disabled", Slug = "disabled", IsActive = false, CreatedBy = SentinelActors.System };
        context.Roles.Add(role);
        context.UserRoles.Add(new UserRole { UserId = userId, Role = role, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetActiveRoleIdsByUserIdAsync(userId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveRoleIdsByUserIdAsync_UserRoleSoftDeleted_ReturnsEmpty()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var role = new Role { Name = "Admin", Slug = "admin", IsActive = true, CreatedBy = SentinelActors.System };
        context.Roles.Add(role);
        var userRole = new UserRole { UserId = userId, Role = role, CreatedBy = SentinelActors.System };
        context.UserRoles.Add(userRole);
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        repository.SoftDelete(userRole);
        await context.SaveChangesAsync();

        var result = await repository.GetActiveRoleIdsByUserIdAsync(userId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveRoleIdsByUserIdAsync_UserWithNoRoles_ReturnsEmpty()
    {
        using var context = CreateContext();
        var repository = new UserRoleRepository(context);

        var result = await repository.GetActiveRoleIdsByUserIdAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActiveRoleIdsByUserIdAsync_MultipleActiveRoles_ReturnsDistinctRoleIds()
    {
        using var context = CreateContext();
        var userId = Guid.NewGuid();
        var roleA = new Role { Name = "Role A", Slug = "role-a", IsActive = true, CreatedBy = SentinelActors.System };
        var roleB = new Role { Name = "Role B", Slug = "role-b", IsActive = true, CreatedBy = SentinelActors.System };
        context.Roles.AddRange(roleA, roleB);
        context.UserRoles.AddRange(
            new UserRole { UserId = userId, Role = roleA, CreatedBy = SentinelActors.System },
            new UserRole { UserId = userId, Role = roleB, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        var repository = new UserRoleRepository(context);
        var result = await repository.GetActiveRoleIdsByUserIdAsync(userId);

        Assert.Equal(2, result.Count);
        Assert.Contains(roleA.Id, result);
        Assert.Contains(roleB.Id, result);
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
