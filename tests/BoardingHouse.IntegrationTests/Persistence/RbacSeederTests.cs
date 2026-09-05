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
        var grants = admin.RolePermissions.Select(rp => (rp.Permission!.Resource, rp.Permission!.Action)).ToHashSet();

        var allPermissions = (await context.Permissions
            .Select(p => new ValueTuple<string, string>(p.Resource, p.Action))
            .ToListAsync()).ToHashSet();

        Assert.Equal(allPermissions, grants);
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
}
