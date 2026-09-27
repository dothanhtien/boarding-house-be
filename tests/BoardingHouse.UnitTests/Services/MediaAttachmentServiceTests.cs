using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardingHouse.UnitTests.Services;

public class MediaAttachmentServiceTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private readonly Mock<IMediaAssetRepository> _mediaAssetRepository = new();
    private readonly Mock<IStorageProvider> _storageProvider = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly MediaAttachmentService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly StoredFile _storedFile = new("root/users/x/new", "https://storage.test/new", 12);

    public MediaAttachmentServiceTests()
    {
        _currentUserAccessor
            .SetupGet(a => a.RequiredUser)
            .Returns(new User
            {
                Email = "actor@test.com",
                PasswordHash = "hashed-password",
                FullName = "Actor",
                CreatedBy = SentinelActors.System
            });
        _storageProvider.SetupGet(p => p.Provider).Returns(StorageProvider.Cloudinary);
        _storageProvider
            .Setup(p => p.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_storedFile);

        _service = new MediaAttachmentService(
            _mediaAssetRepository.Object,
            new MediaUploader(_storageProvider.Object, _currentUserAccessor.Object, NullLogger<MediaUploader>.Instance),
            NullLogger<MediaAttachmentService>.Instance);
    }

    private static IFormFile NewFile(byte[] content, string contentType = "image/png") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "avatar", "me.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private MediaAsset ExistingAvatar() => new()
    {
        FileName = "old.png",
        StorageProvider = StorageProvider.Cloudinary,
        StorageKey = "root/users/x/old",
        FileUrl = "https://storage.test/old",
        MimeType = "image/png",
        FileSize = 12,
        EntityType = MediaAssetEntityType.UserAvatar,
        EntityId = _userId,
        CreatedBy = SentinelActors.System
    };

    private void SetupCurrent(MediaAsset? asset) =>
        _mediaAssetRepository
            .Setup(r => r.GetByEntityAsync(MediaAssetEntityType.UserAvatar, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

    [Fact]
    public async Task StageReplaceAsync_WithExistingAsset_SoftDeletesItAndStagesLinkedNewAsset()
    {
        var existing = ExistingAvatar();
        SetupCurrent(existing);

        var change = await _service.StageReplaceAsync(
            MediaAssetEntityType.UserAvatar, _userId, NewFile(PngBytes), $"users/{_userId}", organizationId: null);

        Assert.Same(existing, change.Removed);
        Assert.NotNull(change.Added);
        Assert.Equal(MediaAssetEntityType.UserAvatar, change.Added.EntityType);
        Assert.Equal(_userId, change.Added.EntityId);
        Assert.Null(change.Added.OrganizationId);
        Assert.Equal(_storedFile.StorageKey, change.Added.StorageKey);
        _storageProvider.Verify(p => p.UploadAsync(
            It.IsAny<Stream>(), "me.png", "image/png", $"users/{_userId}", It.IsAny<CancellationToken>()), Times.Once);
        _mediaAssetRepository.Verify(r => r.SoftDelete(existing), Times.Once);
        _mediaAssetRepository.Verify(r => r.AddAsync(change.Added, It.IsAny<CancellationToken>()), Times.Once);
        // Staging never deletes files: the old one goes only after the caller commits
        _storageProvider.Verify(p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StageReplaceAsync_WithoutExistingAsset_OnlyStagesNewAsset()
    {
        SetupCurrent(null);

        var change = await _service.StageReplaceAsync(
            MediaAssetEntityType.UserAvatar, _userId, NewFile(PngBytes), $"users/{_userId}", organizationId: null);

        Assert.Null(change.Removed);
        Assert.NotNull(change.Added);
        _mediaAssetRepository.Verify(r => r.SoftDelete(It.IsAny<MediaAsset>()), Times.Never);
    }

    [Fact]
    public async Task StageReplaceAsync_LookupFailsAfterUpload_DeletesUploadedFileAndRethrows()
    {
        var original = new InvalidOperationException("db down");
        _mediaAssetRepository
            .Setup(r => r.GetByEntityAsync(It.IsAny<MediaAssetEntityType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(original);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StageReplaceAsync(
            MediaAssetEntityType.UserAvatar, _userId, NewFile(PngBytes), $"users/{_userId}", organizationId: null));

        Assert.Same(original, thrown);
        _storageProvider.Verify(p => p.DeleteAsync(_storedFile.StorageKey, "image/png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StageRemoveAsync_WithExistingAsset_SoftDeletesIt()
    {
        var existing = ExistingAvatar();
        SetupCurrent(existing);

        var change = await _service.StageRemoveAsync(MediaAssetEntityType.UserAvatar, _userId);

        Assert.Same(existing, change.Removed);
        Assert.Null(change.Added);
        _mediaAssetRepository.Verify(r => r.SoftDelete(existing), Times.Once);
    }

    [Fact]
    public async Task StageRemoveAsync_WithoutExistingAsset_ReturnsNone()
    {
        SetupCurrent(null);

        var change = await _service.StageRemoveAsync(MediaAssetEntityType.UserAvatar, _userId);

        Assert.Same(MediaAttachmentChange.None, change);
        _mediaAssetRepository.Verify(r => r.SoftDelete(It.IsAny<MediaAsset>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_DeletesOnlyTheRemovedFile()
    {
        var removed = ExistingAvatar();
        var added = ExistingAvatar();
        added.StorageKey = "root/users/x/new";

        await _service.CompleteAsync(new MediaAttachmentChange(added, removed));

        _storageProvider.Verify(p => p.DeleteAsync(removed.StorageKey, removed.MimeType, It.IsAny<CancellationToken>()), Times.Once);
        _storageProvider.Verify(p => p.DeleteAsync(added.StorageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_StorageDeleteFails_DoesNotThrow()
    {
        _storageProvider
            .Setup(p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AppInternalException("storage down"));

        await _service.CompleteAsync(new MediaAttachmentChange(null, ExistingAvatar()));
    }

    [Fact]
    public async Task DiscardAsync_DeletesOnlyTheAddedFile()
    {
        var removed = ExistingAvatar();
        var added = ExistingAvatar();
        added.StorageKey = "root/users/x/new";

        await _service.DiscardAsync(new MediaAttachmentChange(added, removed));

        _storageProvider.Verify(p => p.DeleteAsync(added.StorageKey, added.MimeType, It.IsAny<CancellationToken>()), Times.Once);
        _storageProvider.Verify(p => p.DeleteAsync(removed.StorageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
