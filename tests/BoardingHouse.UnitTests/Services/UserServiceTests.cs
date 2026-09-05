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
        _context.Users.AddRange(NewUser("a@test.com"), NewUser("b@test.com"));
        await _context.SaveChangesAsync();

        var result = await _userService.GetAllAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, u => u.Email == "a@test.com");
        Assert.Contains(result, u => u.Email == "b@test.com");
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsUserResponse()
    {
        var user = NewUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var result = await _userService.GetByIdAsync(user.Id);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsNotFoundAppException()
    {
        await Assert.ThrowsAsync<NotFoundAppException>(() => _userService.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_EmailOrPhoneAlreadyInUse_ThrowsConflictAppException()
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

        await Assert.ThrowsAsync<ConflictAppException>(() => _userService.CreateAsync(request));
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
    public async Task CreateAsync_ConcurrentUniqueViolation_ThrowsConflictAppException()
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

        await Assert.ThrowsAsync<ConflictAppException>(() => service.CreateAsync(request));
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
    public async Task UpdateAsync_UnknownId_ThrowsNotFoundAppException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var request = new UpdateUserRequest { FullName = "New Name", IsActive = true };

        await Assert.ThrowsAsync<NotFoundAppException>(() => _userService.UpdateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesFields_SavesChanges_AndInvalidatesCache()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new UpdateUserRequest { Phone = "0900000000", FullName = "New Name", IsActive = false };

        var response = await _userService.UpdateAsync(user.Id, request);

        Assert.Equal("New Name", response.FullName);
        Assert.Equal("0900000000", user.Phone);
        Assert.False(user.IsActive);
        _userRepository.Verify(r => r.Update(user), Times.Once);
        _userCache.Verify(c => c.InvalidateAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ThrowsNotFoundAppException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() => _userService.DeleteAsync(Guid.NewGuid()));
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
