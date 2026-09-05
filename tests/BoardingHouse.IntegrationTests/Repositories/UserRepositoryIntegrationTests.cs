using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Repositories;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.IntegrationTests.Repositories;

public class UserRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_PersistsUser_WithSnakeCaseColumns()
    {
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        var emailFromDb = await context.Database
            .SqlQueryRaw<string>("SELECT email AS \"Value\" FROM users WHERE id = {0}", user.Id)
            .SingleAsync();

        Assert.Equal("user@test.com", emailFromDb);
    }

    [Fact]
    public async Task SaveChangesAsync_TwoUsersWithSamePhone_ThrowsDbUpdateException()
    {
        await using var context = fixture.CreateContext();
        context.Users.Add(new User { Email = "a@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "A", CreatedBy = SentinelActors.System });
        await context.SaveChangesAsync();

        await using var secondContext = fixture.CreateContext();
        secondContext.Users.Add(new User { Email = "b@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "B", CreatedBy = SentinelActors.System });

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_TwoUsersWithNullPhone_Succeeds()
    {
        await using var context = fixture.CreateContext();
        context.Users.Add(new User { Email = "a@test.com", PasswordHash = "hashed-password", Phone = null, FullName = "A", CreatedBy = SentinelActors.System });
        context.Users.Add(new User { Email = "b@test.com", PasswordHash = "hashed-password", Phone = null, FullName = "B", CreatedBy = SentinelActors.System });

        await context.SaveChangesAsync();

        var count = await context.Users.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Remove_SoftDeletesUser_PersistsDeletedAtInDb()
    {
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        repository.SoftDelete(user);
        await context.SaveChangesAsync();

        var deletedAt = await context.Database
            .SqlQueryRaw<DateTimeOffset?>("SELECT deleted_at AS \"Value\" FROM users WHERE id = {0}", user.Id)
            .SingleAsync();

        Assert.NotNull(deletedAt);
    }

    [Fact]
    public async Task HardDeleteAsync_ActiveUser_RemovesRowFromDatabase()
    {
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        await repository.HardDeleteAsync(user.Id);

        var count = await context.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM users WHERE id = {0}", user.Id)
            .SingleAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task HardDeleteAsync_SoftDeletedUser_RemovesRowFromDatabase()
    {
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        repository.SoftDelete(user);
        await context.SaveChangesAsync();

        await repository.HardDeleteAsync(user.Id);

        var count = await context.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM users WHERE id = {0}", user.Id)
            .SingleAsync();
        Assert.Equal(0, count);
    }
}
