using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests.Persistence;

public class AuditableEntitySaveChangesInterceptorTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_NewEntity_SetsCreatedAt()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        Assert.True(user.CreatedAt > DateTimeOffset.MinValue);
        Assert.Null(user.UpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_SetsUpdatedAt_KeepsCreatedAt()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var createdAt = user.CreatedAt;

        user.FullName = "Updated Name";
        context.Users.Update(user);
        await context.SaveChangesAsync();

        Assert.Equal(createdAt, user.CreatedAt);
        Assert.NotNull(user.UpdatedAt);
    }
}
