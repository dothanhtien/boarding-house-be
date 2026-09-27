using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Configurations;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Caching;
using BoardingHouse.Api.Services.Storage;
using BoardingHouse.UnitTests.Common;
using Microsoft.AspNetCore.Http;
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
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMediaAttachmentService> _mediaAttachmentService = new();
    private readonly Mock<IStorageProvider> _storageProvider = new();
    private readonly UserService _userService;

    public UserServiceTests()
    {
        MapsterTestSupport.EnsureUserMappingRegistered();

        _storageProvider
            .Setup(p => p.GetUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string key, string _) => $"https://storage.test/{key}");

        _userService = new UserService(
            _userRepository.Object,
            _unitOfWork.Object,
            _userCache.Object,
            _mediaAttachmentService.Object,
            _storageProvider.Object,
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

    private static MediaAsset NewAvatarAsset(string storageKey) => new()
    {
        FileName = "avatar.png",
        StorageProvider = StorageProvider.Cloudinary,
        StorageKey = storageKey,
        FileUrl = $"https://cdn.test/{storageKey}",
        MimeType = "image/png",
        EntityType = MediaAssetEntityType.UserAvatar,
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public async Task GetAllAsync_ReturnsAllUsersAsResponses()
    {
        var users = new List<EntityWithMedia<User>> { new(NewUser("a@test.com"), null), new(NewUser("b@test.com"), null) };
        _userRepository
            .Setup(r => r.SearchAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<PageRequest>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<User, object>>>(), It.IsAny<SortOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<EntityWithMedia<User>> { Items = users, Page = 1, PageSize = 20, TotalItems = users.Count });

        var result = await _userService.GetAllAsync(new UserListQuery());

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, u => u.Email == "a@test.com");
        Assert.Contains(result.Items, u => u.Email == "b@test.com");
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsUserResponse()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var result = await _userService.GetByIdAsync(user.Id);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsAppNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EntityWithMedia<User>?)null);

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
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NewEmail_NormalizesEmail_HashesPassword_AndPersists()
    {
        _userRepository
            .Setup(r => r.ExistsByEmailOrPhoneAsync("new@test.com", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        SetupSaveChangesThrows(new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505"));

        var request = new CreateUserRequest
        {
            Email = "new@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.CreateAsync(request));
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

        SetupSaveChangesThrows(new PostgresException("connection failure", "ERROR", "ERROR", "08006"));

        var request = new CreateUserRequest
        {
            Email = "new@test.com",
            FullName = "Test User",
            Password = "password",
            PasswordConfirmation = "password"
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => _userService.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsAppNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EntityWithMedia<User>?)null);

        var request = new UpdateUserRequest { FullName = "New Name", IsActive = true };

        await Assert.ThrowsAsync<AppNotFoundException>(() => _userService.UpdateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesFields_SavesChanges_AndInvalidatesCache()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        _userRepository.Setup(r => r.ExistsByEmailExcludingUserAsync("new@test.com", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var request = new UpdateUserRequest { Email = "new@test.com", Phone = "0900000000", FullName = "New Name", IsActive = false };

        var response = await _userService.UpdateAsync(user.Id, request);

        Assert.Equal("new@test.com", response.Email);
        Assert.Equal("New Name", response.FullName);
        Assert.Equal("0900000000", user.Phone);
        Assert.False(user.IsActive);
        _userRepository.Verify(r => r.Update(user), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _userCache.Verify(c => c.InvalidateAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentUniqueViolation_ThrowsAppConflictException()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        _userRepository.Setup(r => r.ExistsByEmailExcludingUserAsync("new@test.com", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        SetupSaveChangesThrows(new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505"));

        var request = new UpdateUserRequest { Email = "new@test.com" };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.UpdateAsync(user.Id, request));
        _userCache.Verify(c => c.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_OnlyIsActiveProvided_KeepsEmailAndFullNameUnchanged()
    {
        var user = NewUser();
        var originalEmail = user.Email;
        var originalFullName = user.FullName;
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

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
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var request = new UpdateUserRequest { FullName = "New Name" };

        var response = await _userService.UpdateAsync(user.Id, request);

        Assert.Equal("0900000000", user.Phone);
        Assert.Equal("New Name", response.FullName);
    }

    [Fact]
    public async Task UpdateAsync_SameEmailAsCurrent_DoesNotCheckDuplicate()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var request = new UpdateUserRequest { Email = user.Email, FullName = "New Name" };

        await _userService.UpdateAsync(user.Id, request);

        _userRepository.Verify(r => r.ExistsByEmailExcludingUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_EmailAlreadyUsedByAnotherUser_ThrowsAppConflictException()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        _userRepository.Setup(r => r.ExistsByEmailExcludingUserAsync("taken@test.com", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var request = new UpdateUserRequest { Email = "taken@test.com" };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.UpdateAsync(user.Id, request));
        _userRepository.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_SamePhoneAsCurrent_DoesNotCheckDuplicate()
    {
        var user = NewUser();
        user.Phone = "0900000000";
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));

        var request = new UpdateUserRequest { Phone = "0900000000", FullName = "New Name" };

        await _userService.UpdateAsync(user.Id, request);

        _userRepository.Verify(r => r.ExistsByPhoneExcludingUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PhoneAlreadyUsedByAnotherUser_ThrowsAppConflictException()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        _userRepository.Setup(r => r.ExistsByPhoneExcludingUserAsync("0911111111", user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var request = new UpdateUserRequest { Phone = "0911111111" };

        await Assert.ThrowsAsync<AppConflictException>(() => _userService.UpdateAsync(user.Id, request));
        _userRepository.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _userCache.Verify(c => c.InvalidateAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ExistingUser_KeepsAvatar()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _userService.DeleteAsync(user.Id);

        _mediaAttachmentService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAllAsync_BuildsAvatarUrlsFromStoredMediaLocation()
    {
        var withAvatar = NewUser("a@test.com");
        var withoutAvatar = NewUser("b@test.com");
        _userRepository
            .Setup(r => r.SearchAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<PageRequest>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<User, object>>>(), It.IsAny<SortOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<EntityWithMedia<User>>
            {
                Items = [new(withAvatar, new MediaLocation("root/users/a", "image/png")), new(withoutAvatar, null)],
                Page = 1,
                PageSize = 20,
                TotalItems = 2
            });

        var result = await _userService.GetAllAsync(new UserListQuery());

        Assert.Equal("https://storage.test/root/users/a", result.Items.Single(u => u.Id == withAvatar.Id).AvatarUrl);
        Assert.Null(result.Items.Single(u => u.Id == withoutAvatar.Id).AvatarUrl);
    }

    [Fact]
    public async Task GetByIdAsync_UserWithAvatar_ReturnsDerivedAvatarUrl()
    {
        var user = NewUser();
        _userRepository
            .Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityWithMedia<User>(user, new MediaLocation("root/users/x/avatar", "image/png")));

        var result = await _userService.GetByIdAsync(user.Id);

        Assert.Equal("https://storage.test/root/users/x/avatar", result.AvatarUrl);
    }

    [Fact]
    public async Task UpdateAsync_KeepsCurrentAvatarUrl_AndDoesNotTouchMedia()
    {
        var user = NewUser();
        _userRepository
            .Setup(r => r.GetByIdWithAvatarAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityWithMedia<User>(user, new MediaLocation("root/users/x/current", "image/png")));

        var response = await _userService.UpdateAsync(user.Id, new UpdateUserRequest { FullName = "New Name" });

        Assert.Equal("https://storage.test/root/users/x/current", response.AvatarUrl);
        _mediaAttachmentService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAvatarAsync_UnknownUser_ThrowsAppNotFoundException_AndUploadsNothing()
    {
        _userRepository.Setup(r => r.GetByIdWithRolesAndOrganizationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EntityWithMedia<User>?)null);

        await Assert.ThrowsAsync<AppNotFoundException>(() =>
            _userService.UpdateAvatarAsync(Guid.NewGuid(), new UpdateUserAvatarRequest { File = NewAvatarFile() }));

        _mediaAttachmentService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateAvatarAsync_StagesReplace_SavesThenCompletes_AndReturnsNewUrl()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithRolesAndOrganizationsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        var file = NewAvatarFile();
        var change = new MediaAttachmentChange(NewAvatarAsset("root/users/x/new"), NewAvatarAsset("root/users/x/old"));
        _mediaAttachmentService
            .Setup(s => s.StageReplaceAsync(
                MediaAssetEntityType.UserAvatar, user.Id, file, $"users/{user.Id}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(change);

        var response = await _userService.UpdateAvatarAsync(user.Id, new UpdateUserAvatarRequest { File = file });

        Assert.Equal("https://storage.test/root/users/x/new", response.AvatarUrl);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mediaAttachmentService.Verify(s => s.CompleteAsync(change), Times.Once);
        _mediaAttachmentService.Verify(s => s.DiscardAsync(It.IsAny<MediaAttachmentChange>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAvatarAsync_SaveFails_DiscardsStagedAvatar_AndRethrows()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithRolesAndOrganizationsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        var change = new MediaAttachmentChange(NewAvatarAsset("root/users/x/new"), null);
        SetupStageReplaceReturns(change);
        var original = new InvalidOperationException("db down");
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(original);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _userService.UpdateAvatarAsync(user.Id, new UpdateUserAvatarRequest { File = NewAvatarFile() }));

        Assert.Same(original, thrown);
        _mediaAttachmentService.Verify(s => s.DiscardAsync(change), Times.Once);
        _mediaAttachmentService.Verify(s => s.CompleteAsync(It.IsAny<MediaAttachmentChange>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAvatarAsync_ConcurrentAvatarChange_ThrowsAvatarConflict_AndDiscardsStagedAvatar()
    {
        var user = NewUser();
        _userRepository.Setup(r => r.GetByIdWithRolesAndOrganizationsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new EntityWithMedia<User>(user, null));
        var change = new MediaAttachmentChange(NewAvatarAsset("root/users/x/new"), null);
        SetupStageReplaceReturns(change);
        SetupSaveChangesThrows(new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", "23505",
            constraintName: MediaAssetConfiguration.EntityTypeEntityIdUniqueIndex));

        var ex = await Assert.ThrowsAsync<AppConflictException>(() =>
            _userService.UpdateAvatarAsync(user.Id, new UpdateUserAvatarRequest { File = NewAvatarFile() }));

        Assert.Contains("Avatar", ex.Message);
        _mediaAttachmentService.Verify(s => s.DiscardAsync(change), Times.Once);
        _mediaAttachmentService.Verify(s => s.CompleteAsync(It.IsAny<MediaAttachmentChange>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAvatarAsync_UnknownUser_ThrowsAppNotFoundException()
    {
        _userRepository.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<AppNotFoundException>(() => _userService.DeleteAvatarAsync(Guid.NewGuid()));

        _mediaAttachmentService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAvatarAsync_WithAvatar_StagesRemove_SavesThenCompletes()
    {
        var userId = Guid.NewGuid();
        _userRepository.Setup(r => r.ExistsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var change = new MediaAttachmentChange(null, NewAvatarAsset("root/users/x/old"));
        _mediaAttachmentService
            .Setup(s => s.StageRemoveAsync(MediaAssetEntityType.UserAvatar, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(change);

        await _userService.DeleteAvatarAsync(userId);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mediaAttachmentService.Verify(s => s.CompleteAsync(change), Times.Once);
    }

    [Fact]
    public async Task DeleteAvatarAsync_WithoutAvatar_DoesNotSave()
    {
        var userId = Guid.NewGuid();
        _userRepository.Setup(r => r.ExistsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mediaAttachmentService
            .Setup(s => s.StageRemoveAsync(MediaAssetEntityType.UserAvatar, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaAttachmentChange.None);

        await _userService.DeleteAvatarAsync(userId);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mediaAttachmentService.Verify(s => s.CompleteAsync(It.IsAny<MediaAttachmentChange>()), Times.Never);
    }

    private void SetupStageReplaceReturns(MediaAttachmentChange change) =>
        _mediaAttachmentService
            .Setup(s => s.StageReplaceAsync(
                It.IsAny<MediaAssetEntityType>(), It.IsAny<Guid>(), It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(change);

    private static IFormFile NewAvatarFile() =>
        new FormFile(Stream.Null, 0, 12, "file", "me.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

    private void SetupSaveChangesThrows(Exception inner) =>
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Save failed", inner));
}
