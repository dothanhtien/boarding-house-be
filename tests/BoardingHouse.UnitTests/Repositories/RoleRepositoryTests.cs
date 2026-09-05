using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests.Repositories;

public class RoleRepositoryTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static Role NewRole(string slug) => new()
    {
        Name = slug,
        Slug = slug,
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public async Task GetBySlugAsync_ExistingSlug_ReturnsRole()
    {
        using var context = CreateContext();
        var role = NewRole("admin");
        context.Roles.Add(role);
        await context.SaveChangesAsync();

        var repository = new RoleRepository(context);
        var result = await repository.GetBySlugAsync("admin");

        Assert.NotNull(result);
        Assert.Equal(role.Id, result!.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_UnknownSlug_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new RoleRepository(context);

        var result = await repository.GetBySlugAsync("unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsRole()
    {
        using var context = CreateContext();
        var role = NewRole("admin");
        context.Roles.Add(role);
        await context.SaveChangesAsync();

        var repository = new RoleRepository(context);
        var result = await repository.GetByIdAsync(role.Id);

        Assert.NotNull(result);
        Assert.Equal(role.Id, result!.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllRoles()
    {
        using var context = CreateContext();
        context.Roles.AddRange(NewRole("admin"), NewRole("tenant"));
        await context.SaveChangesAsync();

        var repository = new RoleRepository(context);
        var result = await repository.GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task AddAsync_NewRole_PersistsToDatabase()
    {
        using var context = CreateContext();
        var repository = new RoleRepository(context);
        var role = NewRole("admin");

        await repository.AddAsync(role);
        await context.SaveChangesAsync();

        var result = await context.Roles.FirstOrDefaultAsync(r => r.Id == role.Id);
        Assert.NotNull(result);
    }
}
