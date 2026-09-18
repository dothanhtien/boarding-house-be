using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.IntegrationTests.Persistence;

public class RbacSeederTests(PostgresContainerFixture fixture) : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicatePermissionsOrRoles()
    {
        await using var context = fixture.CreateContext();

        await RbacSeeder.SeedAsync(context);
        var permissionCountAfterFirstRun = await context.Permissions.CountAsync();
        var roleCountAfterFirstRun = await context.Roles.CountAsync();

        await RbacSeeder.SeedAsync(context);
        var permissionCountAfterSecondRun = await context.Permissions.CountAsync();
        var roleCountAfterSecondRun = await context.Roles.CountAsync();

        Assert.Equal(permissionCountAfterFirstRun, permissionCountAfterSecondRun);
        Assert.Equal(roleCountAfterFirstRun, roleCountAfterSecondRun);
    }

    [Fact]
    public async Task SeedAsync_CreatesPlatformAdmin_WithAllPermissions()
    {
        await using var context = fixture.CreateContext();

        await RbacSeeder.SeedAsync(context);

        var admin = await context.Roles
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .SingleAsync(r => r.Slug == "platform_admin");

        Assert.True(admin.IsSystem);
        Assert.Equal(RoleScope.Platform, admin.Scope);
        var grants = admin.RolePermissions.Select(rp => (rp.Permission!.Resource, rp.Permission!.Action)).ToHashSet();

        var allPermissions = (await context.Permissions
            .Select(p => new ValueTuple<string, string>(p.Resource, p.Action))
            .ToListAsync()).ToHashSet();

        Assert.Equal(allPermissions, grants);
    }

    [Fact]
    public async Task SeedAsync_CreatesPlatformStaff_WithPlatformScope()
    {
        await using var context = fixture.CreateContext();

        await RbacSeeder.SeedAsync(context);

        var staff = await context.Roles.SingleAsync(r => r.Slug == "platform_staff");

        Assert.True(staff.IsSystem);
        Assert.Equal(RoleScope.Platform, staff.Scope);
    }

    [Fact]
    public async Task SeedAsync_RunOnSeparateContexts_StillIdempotent()
    {
        int permissionCountAfterFirstRun, roleCountAfterFirstRun;
        await using (var firstRun = fixture.CreateContext())
        {
            await RbacSeeder.SeedAsync(firstRun);
            permissionCountAfterFirstRun = await firstRun.Permissions.CountAsync();
            roleCountAfterFirstRun = await firstRun.Roles.CountAsync();
        }

        await using var secondRun = fixture.CreateContext();
        await RbacSeeder.SeedAsync(secondRun);

        var permissionCountAfterSecondRun = await secondRun.Permissions.CountAsync();
        var roleCountAfterSecondRun = await secondRun.Roles.CountAsync();
        Assert.Equal(permissionCountAfterFirstRun, permissionCountAfterSecondRun);
        Assert.Equal(roleCountAfterFirstRun, roleCountAfterSecondRun);
    }

    [Fact]
    public async Task SeedAsync_PermissionGrantedOutsideRoleSeeds_RevokesFromRole_ButKeepsPermissionDefinition()
    {
        await using var context = fixture.CreateContext();

        await RbacSeeder.SeedAsync(context);

        var staff = await context.Roles.FirstAsync(r => r.Slug == "platform_staff");
        var userCreate = await context.Permissions.SingleAsync(p => p.Resource == "user" && p.Action == "create");
        context.RolePermissions.Add(new RolePermission { RoleId = staff.Id, PermissionId = userCreate.Id, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        await RbacSeeder.SeedAsync(context);

        var after = await context.Roles.Include(r => r.RolePermissions)
            .FirstAsync(r => r.Slug == "platform_staff");
        Assert.DoesNotContain(after.RolePermissions, rp => rp.PermissionId == userCreate.Id);
        Assert.True(await context.Permissions.AnyAsync(p => p.Id == userCreate.Id));
    }

    [Fact]
    public async Task SeedAsync_PermissionNotInPermissionSeeds_DeletesPermission_AndCascadesRolePermission()
    {
        await using var context = fixture.CreateContext();

        await RbacSeeder.SeedAsync(context);
        var staff = await context.Roles.FirstAsync(r => r.Slug == "platform_staff");

        var stray = new Permission { Resource = "stray", Action = "test", CreatedBy = SentinelActors.System };
        context.Permissions.Add(stray);
        await context.SaveChangesAsync();
        context.RolePermissions.Add(new RolePermission { RoleId = staff.Id, PermissionId = stray.Id, CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        await RbacSeeder.SeedAsync(context);

        Assert.False(await context.Permissions.AnyAsync(p => p.Id == stray.Id));
        Assert.False(await context.RolePermissions.AnyAsync(rp => rp.PermissionId == stray.Id));
    }
}
