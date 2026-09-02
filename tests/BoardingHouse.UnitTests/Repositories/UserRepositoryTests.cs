using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests.Repositories;

public class UserRepositoryTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetByEmailAsync_ExistingEmail_ReturnsUser()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.GetByEmailAsync("user@test.com");

        Assert.NotNull(result);
        Assert.Equal(user.Id, result!.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_UnknownEmail_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new UserRepository(context);

        var result = await repository.GetByEmailAsync("unknown@test.com");

        Assert.Null(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_MatchingEmail_ReturnsTrue()
    {
        using var context = CreateContext();
        context.Users.Add(new User { Email = "taken@test.com", PasswordHash = "hashed-password", FullName = "Test User" });
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("taken@test.com", null);

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_MatchingPhone_ReturnsTrue()
    {
        using var context = CreateContext();
        context.Users.Add(new User { Email = "other@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "Test User" });
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("new@test.com", "0900000000");

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_NoMatch_ReturnsFalse()
    {
        using var context = CreateContext();
        context.Users.Add(new User { Email = "other@test.com", PasswordHash = "hashed-password", FullName = "Test User" });
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("new@test.com", null);

        Assert.False(result);
    }

    [Fact]
    public async Task Remove_SoftDeletesUser_ExcludedFromDefaultQuery()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        repository.SoftDelete(user);
        await context.SaveChangesAsync();

        var result = await repository.GetByEmailAsync("user@test.com");

        Assert.Null(result);
    }
}
