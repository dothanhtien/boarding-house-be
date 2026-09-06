using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests.Repositories;

public class RefreshTokenRepositoryTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static User NewUser(string email) => new()
    {
        Email = email,
        PasswordHash = "hashed-password",
        FullName = "Test User",
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public async Task GetByTokenHashAsync_ExistingHash_ReturnsToken()
    {
        using var context = CreateContext();
        var user = NewUser("user@test.com");
        context.Users.Add(user);
        var token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "hash-1",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        var result = await repository.GetByTokenHashAsync("hash-1");

        Assert.NotNull(result);
        Assert.Equal(token.Id, result!.Id);
    }

    [Fact]
    public async Task GetByTokenHashAsync_UnknownHash_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new RefreshTokenRepository(context);

        var result = await repository.GetByTokenHashAsync("unknown-hash");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveByUserIdAsync_ReturnsOnlyNonRevokedNonExpiredTokensForUser()
    {
        using var context = CreateContext();
        var user = NewUser("user@test.com");
        var otherUser = NewUser("other@test.com");
        context.Users.AddRange(user, otherUser);

        var active = new RefreshToken { UserId = user.Id, TokenHash = "active", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        var revoked = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "revoked",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var expired = new RefreshToken { UserId = user.Id, TokenHash = "expired", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var otherUsersToken = new RefreshToken { UserId = otherUser.Id, TokenHash = "other-user", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };

        context.RefreshTokens.AddRange(active, revoked, expired, otherUsersToken);
        await context.SaveChangesAsync();

        var repository = new RefreshTokenRepository(context);
        var result = await repository.GetActiveByUserIdAsync(user.Id);

        Assert.Single(result);
        Assert.Equal(active.Id, result[0].Id);
    }

    [Fact]
    public async Task AddAsync_NewToken_PersistsToDatabase()
    {
        using var context = CreateContext();
        var repository = new RefreshTokenRepository(context);
        var user = NewUser("user@test.com");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "new-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        await repository.AddAsync(token);
        await context.SaveChangesAsync();

        var result = await context.RefreshTokens.FirstOrDefaultAsync(t => t.Id == token.Id);
        Assert.NotNull(result);
    }
}
