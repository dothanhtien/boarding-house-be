using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Api.Common;

namespace BoardingHouse.UnitTests.Services;

public class MediaCollectionServiceTests
{
    private readonly Mock<IMediaAssetRepository> _mediaAssetRepository = new();
    private readonly Mock<IMediaUploader> _mediaUploader = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly MediaCollectionService _service;

    private readonly Guid _organizationId = Guid.NewGuid();

    public MediaCollectionServiceTests()
    {
        _service = new MediaCollectionService(
            _mediaAssetRepository.Object,
            _mediaUploader.Object, _unitOfWork.Object,
            NullLogger<MediaCollectionService>.Instance);
    }

    private static IFileUpload NewFile(string fileName) => NewFile(new MemoryStream([1]), 0, 1, fileName);

    private static IFileUpload NewFile(Stream body, long offset, long length, string fileName) =>
        new FormFileUpload(new FormFile(body, offset, length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        });

    private void SetupUpload(string fileName, Func<Task<MediaAsset>> upload) =>
        _mediaUploader
            .Setup(u => u.UploadAsync(
                It.Is<IFileUpload>(f => f.FileName == fileName), "folder", _organizationId, It.IsAny<CancellationToken>()))
            .Returns(upload);

    private static MediaAsset Asset(string storageKey) => new()
    {
        FileName = $"{storageKey}.png",
        StorageProvider = StorageProvider.Cloudinary,
        StorageKey = storageKey,
        FileUrl = $"https://storage.test/{storageKey}",
        MimeType = "image/png",
        FileSize = 1,
        CreatedBy = SentinelActors.System
    };

    [Fact]
    public async Task AddAsync_AllUploadsSucceed_AttachesUntrackedAssetsTaggedWithEntityTypeInFileOrder()
    {
        SetupTransaction();
        // "a" finishes last, yet must stay first: order decides sort_order and the cover
        SetupUpload("a.png", async () =>
        {
            await Task.Delay(50);
            return Asset("a");
        });
        SetupUpload("b.png", () => Task.FromResult(Asset("b")));

        IReadOnlyList<MediaAsset>? uploaded = null;
        var untracked = false;

        await _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png"), NewFile("b.png")],
            "folder",
            _organizationId,
            (assets, _) =>
            {
                uploaded = assets;
                untracked = _mediaAssetRepository.Invocations.Count == 0;
                return Task.FromResult("attached");
            });

        Assert.Equal(["a", "b"], uploaded!.Select(m => m.StorageKey));
        Assert.All(uploaded!, m =>
        {
            Assert.Equal(MediaAssetEntityType.RoomMedia, m.EntityType);
            Assert.Null(m.EntityId);
        });
        Assert.True(untracked);
    }

    // A request body that fails if two threads read it at the same time, like the shared buffer behind form files
    private sealed class ExclusiveReadStream(byte[] content) : MemoryStream(content)
    {
        private int _readers;

        public override int Read(byte[] buffer, int offset, int count) => Guarded(() => base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer)
        {
            var copy = new byte[buffer.Length];
            var read = Guarded(() => base.Read(copy, 0, copy.Length));
            copy.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        private int Guarded(Func<int> read)
        {
            if (Interlocked.Increment(ref _readers) > 1)
            {
                Interlocked.Decrement(ref _readers);
                throw new InvalidOperationException("Shared request body read concurrently");
            }

            try
            {
                Thread.Sleep(20);   // widen the window so overlapping reads are caught
                return read();
            }
            finally
            {
                Interlocked.Decrement(ref _readers);
            }
        }
    }

    [Fact]
    public async Task AddAsync_FilesSharingOneRequestBody_NeverReadsTheBodyConcurrently()
    {
        SetupTransaction();
        // Mirrors ASP.NET multipart binding: both form files are views over the same buffered body
        var body = new ExclusiveReadStream("aaaabbbb"u8.ToArray());
        var contents = new ConcurrentDictionary<string, string>();
        using var bothStarted = new Barrier(2);
        _mediaUploader
            .Setup(u => u.UploadAsync(It.IsAny<IFileUpload>(), "folder", _organizationId, It.IsAny<CancellationToken>()))
            .Returns((IFileUpload file, string _, Guid? _, CancellationToken _) => Task.Run(() =>
            {
                bothStarted.SignalAndWait(TimeSpan.FromSeconds(5));   // both uploads read at the same moment
                using var stream = file.OpenReadStream();
                var read = new byte[file.Length];
                stream.ReadExactly(read);
                contents[file.FileName] = Encoding.ASCII.GetString(read);
                return Asset(file.FileName);
            }));

        await _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile(body, 0, 4, "a.png"), NewFile(body, 4, 4, "b.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached"));

        Assert.Equal("aaaa", contents["a.png"]);
        Assert.Equal("bbbb", contents["b.png"]);
    }

    [Fact]
    public async Task AddAsync_OneUploadFails_WaitsForOthersDeletesEverySuccessfulUploadAndRethrows()
    {
        SetupUpload("a.png", async () =>
        {
            await Task.Delay(50);   // still running when "b" fails: must be awaited, not abandoned
            return Asset("a");
        });
        SetupUpload("b.png", () => Task.FromException<MediaAsset>(
            new AppValidationException("File content does not match its declared type")));
        SetupUpload("c.png", () => Task.FromResult(Asset("c")));

        await Assert.ThrowsAsync<AppValidationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png"), NewFile("b.png"), NewFile("c.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
        _mediaUploader.Verify(u => u.TryDeleteAsync("c", "image/png"), Times.Once);
        _mediaAssetRepository.VerifyNoOtherCalls();
    }

    // Runs the delegate like the real unit of work, then optionally fails the commit
    private void SetupTransaction(Exception? commitFailure = null) =>
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<string>> operation, CancellationToken ct) =>
            {
                var result = await operation(ct);
                return commitFailure is null ? result : throw commitFailure;
            });

    [Fact]
    public async Task AddAsync_AttachSucceeds_AttachesThenStagesUploadsInOneTransactionAndKeepsFiles()
    {
        SetupUpload("a.png", () => Task.FromResult(Asset("a")));
        SetupUpload("b.png", () => Task.FromResult(Asset("b")));
        SetupTransaction();
        var steps = new List<string>();
        _mediaAssetRepository
            .Setup(r => r.AddAsync(It.IsAny<MediaAsset>(), It.IsAny<CancellationToken>()))
            .Callback((MediaAsset asset, CancellationToken _) => steps.Add($"stage:{asset.StorageKey}"));
        IReadOnlyList<MediaAsset>? attached = null;

        var result = await _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png"), NewFile("b.png")],
            "folder",
            _organizationId,
            (uploaded, _) =>
            {
                attached = uploaded;
                steps.Add("attach");
                return Task.FromResult("attached");
            });

        Assert.Equal("attached", result);
        Assert.Equal(["a", "b"], attached!.Select(m => m.StorageKey));
        // The attach delegate takes the owner's row lock and re-checks limits before anything is staged
        Assert.Equal(["attach", "stage:a", "stage:b"], steps);
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mediaUploader.Verify(u => u.TryDeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_AttachThrows_DiscardsUploadsWithoutStagingAndRethrows()
    {
        SetupUpload("a.png", () => Task.FromResult(Asset("a")));
        SetupTransaction();

        await Assert.ThrowsAsync<AppConflictException>(() => _service.AddAsync<string>(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) => throw new AppConflictException("limit reached")));

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
        _mediaAssetRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddAsync_SaveFails_DiscardsUploadsWithoutCheckingDatabaseAndRethrows()
    {
        SetupUpload("a.png", () => Task.FromResult(Asset("a")));
        SetupTransaction(new PersistenceException("unique violation", new Exception()));

        await Assert.ThrowsAsync<PersistenceException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
        _mediaAssetRepository.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_CommitFailsAndRowsAbsent_DiscardsUploadsAndRethrows()
    {
        var asset = Asset("a");
        SetupUpload("a.png", () => Task.FromResult(asset));
        SetupTransaction(new InvalidOperationException("commit failed"));
        _mediaAssetRepository.Setup(r => r.ExistsAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
    }

    [Fact]
    public async Task AddAsync_CommitFailsButRowsPersisted_KeepsFilesAndRethrows()
    {
        var asset = Asset("a");
        SetupUpload("a.png", () => Task.FromResult(asset));
        SetupTransaction(new InvalidOperationException("connection lost during commit"));
        _mediaAssetRepository.Setup(r => r.ExistsAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaUploader.Verify(u => u.TryDeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_CommitFailsAndCheckFails_KeepsFilesAndRethrowsOriginalError()
    {
        SetupUpload("a.png", () => Task.FromResult(Asset("a")));
        SetupTransaction(new InvalidOperationException("connection lost during commit"));
        _mediaAssetRepository
            .Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database unreachable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaUploader.Verify(u => u.TryDeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_NoFilesAndCommitFails_RethrowsWithoutCheckingDatabase()
    {
        SetupTransaction(new InvalidOperationException("connection lost during commit"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaAssetRepository.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediaUploader.Verify(u => u.TryDeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_UploadFails_NeverOpensTransaction()
    {
        SetupUpload("a.png", () => Task.FromException<MediaAsset>(
            new AppValidationException("File content does not match its declared type")));
        var attachCalled = false;

        await Assert.ThrowsAsync<AppValidationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) =>
            {
                attachCalled = true;
                return Task.FromResult("attached");
            }));

        Assert.False(attachCalled);
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mediaAssetRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddAsync_InsideAnotherTransaction_ThrowsBeforeUploading()
    {
        // A caller's later rollback would orphan the uploads, out of reach of AddAsync's discard handling
        _unitOfWork.Setup(u => u.HasActiveTransaction).Returns(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile("a.png")],
            "folder",
            _organizationId,
            (_, _) => Task.FromResult("attached")));

        _mediaUploader.VerifyNoOtherCalls();
        _mediaAssetRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public void StageRemove_SoftDeletesEveryAssetWithoutTouchingStorage()
    {
        var a = Asset("a");

        _service.StageRemove([a]);

        _mediaAssetRepository.Verify(r => r.SoftDelete(a), Times.Once);
        _mediaUploader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteAsync_DeletesRemovedFiles()
    {
        await _service.CompleteAsync([Asset("a"), Asset("b")]);

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
        _mediaUploader.Verify(u => u.TryDeleteAsync("b", "image/png"), Times.Once);
    }
}
