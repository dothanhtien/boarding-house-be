using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using BoardingHouse.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests.Repositories;

public class UserRepositoryTests
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
    public async Task GetByEmailAsync_DifferentCasing_ReturnsUser()
    {
        using var context = CreateContext();
        var user = new User { Email = "User@Test.com", PasswordHash = "hashed-password", FullName = "Test User" };
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
    public async Task ExistsByEmailOrPhoneAsync_DifferentEmailCasing_ReturnsTrue()
    {
        using var context = CreateContext();
        context.Users.Add(new User { Email = "Taken@Test.com", PasswordHash = "hashed-password", FullName = "Test User" });
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("taken@test.com", null);

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_MatchBelongsToSoftDeletedUser_ReturnsFalse()
    {
        using var context = CreateContext();
        var user = new User { Email = "gone@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "Test User" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        repository.SoftDelete(user);
        await context.SaveChangesAsync();

        var result = await repository.ExistsByEmailOrPhoneAsync("gone@test.com", "0900000000");

        Assert.False(result);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesSoftDeletedUsers()
    {
        using var context = CreateContext();
        var activeUser = new User { Email = "active@test.com", PasswordHash = "hashed-password", FullName = "Active User" };
        var deletedUser = new User { Email = "deleted@test.com", PasswordHash = "hashed-password", FullName = "Deleted User" };
        context.Users.AddRange(activeUser, deletedUser);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        repository.SoftDelete(deletedUser);
        await context.SaveChangesAsync();

        var result = await repository.GetAllAsync();

        Assert.Single(result);
        Assert.Equal(activeUser.Id, result[0].Id);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsUser()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.GetByIdAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(user.Id, result!.Id);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var context = CreateContext();
        var repository = new UserRepository(context);

        var result = await repository.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_NewUser_PersistsToDatabase()
    {
        using var context = CreateContext();
        var repository = new UserRepository(context);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User" };

        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        var result = await context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Update_ModifiedUser_PersistsChanges()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Old Name" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        user.FullName = "New Name";
        repository.Update(user);
        await context.SaveChangesAsync();

        var result = await context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(result);
        Assert.Equal("New Name", result!.FullName);
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

        var deletedUser = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(deletedUser);
        Assert.NotNull(deletedUser!.DeletedAt);
    }
}
