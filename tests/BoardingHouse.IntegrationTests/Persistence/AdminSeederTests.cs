using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.IntegrationTests.Persistence;

public class AdminSeederTests(PostgresContainerFixture fixture) : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private const string Email = "admin@test.com";
    private const string Password = "password123";
    private const string FullName = "Platform Admin";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedAsync_RoleNotSeeded_ThrowsInvalidOperationException()
    {
        await using var context = fixture.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminSeeder.SeedAsync(context, Email, Password, FullName));
    }

    [Fact]
    public async Task SeedAsync_RoleSeeded_CreatesUserWithPlatformAdminRole()
    {
        await using var context = fixture.CreateContext();
        await RbacSeeder.SeedAsync(context);

        await AdminSeeder.SeedAsync(context, Email, Password, FullName);

        var user = await context.Users.SingleAsync(u => u.Email == Email);
        var hasRole = await context.UserRoles
            .Include(ur => ur.Role)
            .AnyAsync(ur => ur.UserId == user.Id && ur.Role!.Slug == "platform_admin");
        Assert.True(hasRole);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_EmailWithMixedCaseAndWhitespace_IsNormalized()
    {
        await using var context = fixture.CreateContext();
        await RbacSeeder.SeedAsync(context);

        await AdminSeeder.SeedAsync(context, "  Admin@Test.com  ", Password, FullName);

        Assert.True(await context.Users.AnyAsync(u => u.Email == Email));
    }

    [Fact]
    public async Task SeedAsync_UserAlreadyHasRole_ThrowsInvalidOperationException()
    {
        await using var context = fixture.CreateContext();
        await RbacSeeder.SeedAsync(context);
        await AdminSeeder.SeedAsync(context, Email, Password, FullName);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminSeeder.SeedAsync(context, Email, Password, FullName));
        Assert.Contains("already has role", exception.Message);
    }

    [Fact]
    public async Task SeedAsync_UserExistsWithoutRole_ThrowsInvalidOperationException()
    {
        await using var context = fixture.CreateContext();
        await RbacSeeder.SeedAsync(context);
        context.Users.Add(new BoardingHouse.Api.Entities.User
        {
            Email = Email,
            PasswordHash = "hashed-password",
            FullName = FullName,
            CreatedBy = BoardingHouse.Api.Common.SentinelActors.System
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminSeeder.SeedAsync(context, Email, Password, FullName));
        Assert.Contains("missing role", exception.Message);
    }
}
