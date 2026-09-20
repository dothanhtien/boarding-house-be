using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace BoardingHouse.UnitTests.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUserCache> _userCache = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly AppDbContext _context;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        _userService = new UserService(
            _userRepository.Object,
            _context,
            _userCache.Object,
            _currentUserAccessor.Object,
            NullLogger<UserService>.Instance);
    }

    private static User NewUser(string email = "user@test.com") => new()
    {
        Email = email,
        PasswordHash = "hashed-password",
        FullName = "Test User",
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public async Task GetAllAsync_ReturnsAllUsersAsResponses()
    {
        var users = new List<User> { NewUser("a@test.com"), NewUser("b@test.com") };
        _userRepository
            .Setup(r => r.SearchAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<PageRequest>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<User, object>>>(), It.IsAny<SortOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<User> { Items = users, Page = 1, PageSize = 20, TotalItems = users.Count });

        var result = await _userService.GetAllAsync(new UserListQuery());

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, u => u.Email == "a@test.com");
        Assert.Contains(result.Items, u => u.Email == "b@test.com");
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsUserResponse()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _userService.GetByIdAsync(user.Id);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsAppNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<AppNotFoundException>(() => _userService.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_EmailOrPhoneAlreadyInUse_ThrowsAppConflictException()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync("taken@test.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CreateUserRequest
        {
            Email = "taken@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_NewEmail_NormalizesEmail_HashesPassword_AndPersists()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync("new@test.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _userRepository
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns<User, CancellationToken>((u, _) =>
            {
                _context.Users.Add(u);
                return Task.CompletedTask;
            });

        var request = new CreateUserRequest
        {
            Email = "  New@Test.com  ",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        var response = await _userService.CreateAsync(request);

        Assert.Equal("new@test.com", response.Email);
        _userRepository.Verify(r => r.AddAsync(
            It.Is<User>(u => u.Email == "new@test.com" && u.PasswordHash != "password" && u.PasswordHash != string.Empty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_NoCurrentUser_SetsCreatedByToSystemActor()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        User? added = null;
        _userRepository
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns<User, CancellationToken>((u, _) =>
            {
                added = u;
                _context.Users.Add(u);
                return Task.CompletedTask;
            });

        var request = new CreateUserRequest
        {
            Email = "new@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await _userService.CreateAsync(request);

        Assert.NotNull(added);
        Assert.Equal(SentinelActors.System, added!.CreatedBy);
    }

    [Fact]
    public async Task CreateAsync_WithCurrentUser_SetsCreatedByToCurrentUserId()
    {
        var currentUser = NewUser("actor@test.com");
        _currentUserAccessor.Setup(a => a.User).Returns(currentUser);
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        User? added = null;
        _userRepository
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns<User, CancellationToken>((u, _) =>
            {
                added = u;
                _context.Users.Add(u);
                return Task.CompletedTask;
            });

        var request = new CreateUserRequest
        {
            Email = "new@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await _userService.CreateAsync(request);

        Assert.NotNull(added);
        Assert.Equal(currentUser.Id, added!.CreatedBy);
    }

    [Fact]
    public async Task CreateAsync_ConcurrentUniqueViolation_ThrowsAppConflictException()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _userRepository
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var postgresException = new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var throwingContext = new ThrowingOnSaveDbContext(options, postgresException);

        var service = new UserService(
            _userRepository.Object,
            throwingContext,
            _userCache.Object,
            _currentUserAccessor.Object,
            NullLogger<UserService>.Instance);

        var request = new CreateUserRequest
        {
            Email = "new@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await Assert.ThrowsAsync<AppConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_NonUniqueViolationDbUpdateException_PropagatesException()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _userRepository
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var postgresException = new PostgresException("connection failure", "ERROR", "ERROR", "08006");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var throwingContext = new ThrowingOnSaveDbContext(options, postgresException);

        var service = new UserService(
            _userRepository.Object,
            throwingContext,
            _userCache.Object,
            _currentUserAccessor.Object,
            NullLogger<UserService>.Instance);

        var request = new CreateUserRequest
        {
            Email = "new@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsAppNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var request = new UpdateUserRequest { FullName = "New Name", IsActive = true };

        await Assert.ThrowsAsync<AppNotFoundException>(() => _userService.UpdateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesFields_SavesChanges_AndInvalidatesCache()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepository.Setup(r => r.ExistsByEmailExcludingUserAsync("new@test.com", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var request = new UpdateUserRequest { Email = "new@test.com", Phone = "0900000000", FullName = "New Name", IsActive = false };

        var response = await _userService.UpdateAsync(user.Id, request);

        Assert.Equal("new@test.com", response.Email);
        Assert.Equal("New Name", response.FullName);
        Assert.Equal("0900000000", user.Phone);
        Assert.False(user.IsActive);
        _userRepository.Verify(r => r.Update(user), Times.Once);
        _userCache.Verify(c => c.InvalidateAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_OnlyIsActiveProvided_KeepsEmailAndFullNameUnchanged()
    {
        var user = NewUser();
        var originalEmail = user.Email;
        var originalFullName = user.FullName;
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new UpdateUserRequest { IsActive = false };

        var response = await _userService.UpdateAsync(user.Id, request);

        Assert.Equal(originalEmail, response.Email);
        Assert.Equal(originalFullName, response.FullName);
        Assert.False(response.IsActive);
        _userRepository.Verify(r => r.ExistsByEmailExcludingUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PhoneNotProvided_KeepsExistingPhone()
    {
        var user = NewUser();
        user.Phone = "0900000000";
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new UpdateUserRequest { FullName = "New Name" };

        var response = await _userService.UpdateAsync(user.Id, request);

        Assert.Equal("0900000000", user.Phone);
        Assert.Equal("New Name", response.FullName);
    }

    [Fact]
    public async Task UpdateAsync_SameEmailAsCurrent_DoesNotCheckDuplicate()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new UpdateUserRequest { Email = user.Email, FullName = "New Name" };

        await _userService.UpdateAsync(user.Id, request);

        _userRepository.Verify(r => r.ExistsByEmailExcludingUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_EmailAlreadyUsedByAnotherUser_ThrowsAppConflictException()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepository.Setup(r => r.ExistsByEmailExcludingUserAsync("taken@test.com", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var request = new UpdateUserRequest { Email = "taken@test.com" };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.UpdateAsync(user.Id, request));
        _userRepository.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_SamePhoneAsCurrent_DoesNotCheckDuplicate()
    {
        var user = NewUser();
        user.Phone = "0900000000";
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new UpdateUserRequest { Phone = "0900000000", FullName = "New Name" };

        await _userService.UpdateAsync(user.Id, request);

        _userRepository.Verify(r => r.ExistsByPhoneExcludingUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PhoneAlreadyUsedByAnotherUser_ThrowsAppConflictException()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepository.Setup(r => r.ExistsByPhoneExcludingUserAsync("0911111111", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var request = new UpdateUserRequest { Phone = "0911111111" };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.UpdateAsync(user.Id, request));
        _userRepository.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ThrowsAppNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<AppNotFoundException>(() => _userService.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_ExistingUser_SoftDeletes_SavesChanges_AndInvalidatesCache()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _userService.DeleteAsync(user.Id);

        _userRepository.Verify(r => r.SoftDelete(user), Times.Once);
        _userCache.Verify(c => c.InvalidateAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class ThrowingOnSaveDbContext(DbContextOptions<AppDbContext> options, Exception inner) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Save failed", inner);
    }
}
