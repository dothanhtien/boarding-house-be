using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Services;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Services;

public class OrganizationScopeAccessorTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetScopeAsync_UserWithoutMembershipOrPlatformPermission_ReturnsNone()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IOrganizationScopeAccessor>();
        var currentUserAccessor = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();

        var user = new User { Email = "no-org@test.com", PasswordHash = "x", FullName = "No Org", CreatedBy = Guid.NewGuid() };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        currentUserAccessor.User = user;

        var result = await accessor.GetScopeAsync("property", "read");

        Assert.False(result.IsUnrestricted);
        Assert.Empty(result.OrganizationIds);
    }

    [Fact]
    public async Task GetScopeAsync_MemberWithMatchingRolePermission_ReturnsOwnOrganizationOnly()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IOrganizationScopeAccessor>();
        var currentUserAccessor = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();

        var creatorId = Guid.NewGuid();
        var user = new User { Email = "member@test.com", PasswordHash = "x", FullName = "Member", CreatedBy = creatorId };
        var organizationA = new Organization { Name = "Org A", CreatedBy = creatorId };
        var organizationB = new Organization { Name = "Org B", CreatedBy = creatorId };
        var role = new Role { Slug = "test_org_role", Name = "Test Org Role", Scope = RoleScope.Organization, CreatedBy = creatorId };
        var permission = new Permission { Resource = "property", Action = "read", CreatedBy = creatorId };
        context.AddRange(user, organizationA, organizationB, role, permission);
        await context.SaveChangesAsync();

        context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, CreatedBy = creatorId });
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationA.Id,
            UserId = user.Id,
            RoleId = role.Id,
            CreatedBy = creatorId
        });
        await context.SaveChangesAsync();
        currentUserAccessor.User = user;

        var result = await accessor.GetScopeAsync("property", "read");

        Assert.False(result.IsUnrestricted);
        Assert.Single(result.OrganizationIds);
        Assert.Contains(organizationA.Id, result.OrganizationIds);
        Assert.DoesNotContain(organizationB.Id, result.OrganizationIds);
    }

    [Fact]
    public async Task GetScopeAsync_PlatformAdmin_ReturnsUnrestricted()
    {
        using var scope = factory.Services.CreateScope();
        var currentUserAccessor = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();
        var accessor = scope.ServiceProvider.GetRequiredService<IOrganizationScopeAccessor>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = new User { Email = "admin-scope@test.com", PasswordHash = "x", FullName = "Admin", CreatedBy = Guid.NewGuid() };
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        await factory.GrantPlatformAdminRoleAsync(admin.Id);
        currentUserAccessor.User = admin;

        var result = await accessor.GetScopeAsync("organization", "read");

        Assert.True(result.IsUnrestricted);
    }
}
