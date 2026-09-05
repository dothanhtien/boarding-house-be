using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Repositories;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.IntegrationTests.Repositories;

public class RefreshTokenRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(string email = "user@test.com") =>
        new() { Email = email, PasswordHash = "hashed-password", FullName = "Test User" };

    private static RefreshToken NewToken(Guid userId, string tokenHash, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null) =>
        new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = revokedAt
        };

    [Fact]
    public async Task GetByTokenHashAsync_ExistingHash_ReturnsToken()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        var token = NewToken(user.Id, "hash-a");
        await repository.AddAsync(token);
        await context.SaveChangesAsync();

        var result = await repository.GetByTokenHashAsync("hash-a");

        Assert.NotNull(result);
        Assert.Equal(token.Id, result!.Id);
    }

    [Fact]
    public async Task GetByTokenHashAsync_UnknownHash_ReturnsNull()
    {
        await using var context = fixture.CreateContext();
        var repository = new RefreshTokenRepository(context);

        var result = await repository.GetByTokenHashAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveByUserIdAsync_ExcludesRevokedTokens()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        await repository.AddAsync(NewToken(user.Id, "active-hash"));
        await repository.AddAsync(NewToken(user.Id, "revoked-hash", revokedAt: DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var result = await repository.GetActiveByUserIdAsync(user.Id);

        Assert.Single(result);
        Assert.Equal("active-hash", result[0].TokenHash);
    }

    [Fact]
    public async Task GetActiveByUserIdAsync_ExcludesExpiredTokens()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        await repository.AddAsync(NewToken(user.Id, "active-hash"));
        await repository.AddAsync(NewToken(user.Id, "expired-hash", expiresAt: DateTimeOffset.UtcNow.AddDays(-1)));
        await context.SaveChangesAsync();

        var result = await repository.GetActiveByUserIdAsync(user.Id);

        Assert.Single(result);
        Assert.Equal("active-hash", result[0].TokenHash);
    }

    [Fact]
    public async Task GetActiveByUserIdAsync_ExcludesOtherUsersTokens()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser("a@test.com");
        var otherUser = NewUser("b@test.com");
        context.Users.AddRange(user, otherUser);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        await repository.AddAsync(NewToken(user.Id, "user-hash"));
        await repository.AddAsync(NewToken(otherUser.Id, "other-user-hash"));
        await context.SaveChangesAsync();

        var result = await repository.GetActiveByUserIdAsync(user.Id);

        Assert.Single(result);
        Assert.Equal("user-hash", result[0].TokenHash);
    }

    [Fact]
    public async Task GetActiveByUserIdAsync_NoActiveTokens_ReturnsEmptyList()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);

        var result = await repository.GetActiveByUserIdAsync(user.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddAsync_PersistsRefreshTokenWithSnakeCaseColumns()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        var token = NewToken(user.Id, "persisted-hash");
        await repository.AddAsync(token);
        await context.SaveChangesAsync();

        var tokenHashFromDb = await context.Database
            .SqlQueryRaw<string>("SELECT token_hash AS \"Value\" FROM refresh_tokens WHERE id = {0}", token.Id)
            .SingleAsync();

        Assert.Equal("persisted-hash", tokenHashFromDb);
    }

    [Fact]
    public async Task RevokedReason_PersistsAndReadsBackCorrectly()
    {
        await using var context = fixture.CreateContext();
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        var token = NewToken(user.Id, "reused-hash", revokedAt: DateTimeOffset.UtcNow);
        token.RevokedReason = RevokedReason.Suspicious;
        await repository.AddAsync(token);
        await context.SaveChangesAsync();

        await using var freshContext = fixture.CreateContext();
        var reloaded = await freshContext.Set<RefreshToken>().SingleAsync(t => t.Id == token.Id);

        Assert.Equal(RevokedReason.Suspicious, reloaded.RevokedReason);
    }
}
