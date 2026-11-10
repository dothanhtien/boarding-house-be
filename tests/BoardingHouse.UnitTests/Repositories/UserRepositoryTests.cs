using BoardingHouse.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Interceptors;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Api.Common.CurrentUser;

namespace BoardingHouse.UnitTests.Repositories;

public class UserRepositoryTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                new CurrentUserAccessor(),
                NullLogger<AuditableEntitySaveChangesInterceptor>.Instance))
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetByEmailAsync_ExistingEmail_ReturnsUser()
    {
        using var context = CreateContext();
        var user = TestEntities.New<User>(new { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System });
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
        var user = TestEntities.New<User>(new { Email = "User@Test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System });
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
        context.Users.Add(TestEntities.New<User>(new { Email = "taken@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System }));
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("taken@test.com", null);

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_MatchingPhone_ReturnsTrue()
    {
        using var context = CreateContext();
        context.Users.Add(TestEntities.New<User>(new { Email = "other@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "Test User", CreatedBy = SentinelActors.System }));
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("new@test.com", "0900000000");

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_NoMatch_ReturnsFalse()
    {
        using var context = CreateContext();
        context.Users.Add(TestEntities.New<User>(new { Email = "other@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System }));
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("new@test.com", null);

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_DifferentEmailCasing_ReturnsTrue()
    {
        using var context = CreateContext();
        context.Users.Add(TestEntities.New<User>(new { Email = "Taken@Test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System }));
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailOrPhoneAsync("taken@test.com", null);

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailOrPhoneAsync_MatchBelongsToSoftDeletedUser_ReturnsFalse()
    {
        using var context = CreateContext();
        var user = TestEntities.New<User>(new { Email = "gone@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "Test User", CreatedBy = SentinelActors.System });
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        repository.SoftDelete(user);
        await context.SaveChangesAsync();

        var result = await repository.ExistsByEmailOrPhoneAsync("gone@test.com", "0900000000");

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsByEmailExcludingUserAsync_MatchingEmailOfOtherUser_ReturnsTrue()
    {
        using var context = CreateContext();
        var otherUser = TestEntities.New<User>(new { Email = "taken@test.com", PasswordHash = "hashed-password", FullName = "Other", CreatedBy = SentinelActors.System });
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailExcludingUserAsync("taken@test.com", Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailExcludingUserAsync_MatchingEmailBelongsToExcludedUser_ReturnsFalse()
    {
        using var context = CreateContext();
        var user = TestEntities.New<User>(new { Email = "self@test.com", PasswordHash = "hashed-password", FullName = "Self", CreatedBy = SentinelActors.System });
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailExcludingUserAsync("self@test.com", user.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsByEmailExcludingUserAsync_DifferentEmailCasing_ReturnsTrue()
    {
        using var context = CreateContext();
        var otherUser = TestEntities.New<User>(new { Email = "Taken@Test.com", PasswordHash = "hashed-password", FullName = "Other", CreatedBy = SentinelActors.System });
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailExcludingUserAsync("taken@test.com", Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByEmailExcludingUserAsync_NoMatch_ReturnsFalse()
    {
        using var context = CreateContext();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByEmailExcludingUserAsync("nobody@test.com", Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsByPhoneExcludingUserAsync_MatchingPhoneOfOtherUser_ReturnsTrue()
    {
        using var context = CreateContext();
        var otherUser = TestEntities.New<User>(new { Email = "other@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "Other", CreatedBy = SentinelActors.System });
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByPhoneExcludingUserAsync("0900000000", Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsByPhoneExcludingUserAsync_MatchingPhoneBelongsToExcludedUser_ReturnsFalse()
    {
        using var context = CreateContext();
        var user = TestEntities.New<User>(new { Email = "self@test.com", PasswordHash = "hashed-password", Phone = "0900000000", FullName = "Self", CreatedBy = SentinelActors.System });
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByPhoneExcludingUserAsync("0900000000", user.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsByPhoneExcludingUserAsync_NoMatch_ReturnsFalse()
    {
        using var context = CreateContext();

        var repository = new UserRepository(context);
        var result = await repository.ExistsByPhoneExcludingUserAsync("0900000000", Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsUser()
    {
        using var context = CreateContext();
        var user = TestEntities.New<User>(new { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System });
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
        var user = TestEntities.New<User>(new { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System });

        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        var result = await context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Remove_SoftDeletesUser_ExcludedFromDefaultQuery()
    {
        using var context = CreateContext();
        var user = TestEntities.New<User>(new { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System });
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
