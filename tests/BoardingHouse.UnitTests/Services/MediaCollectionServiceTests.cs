using System.Collections.Concurrent;
using System.Text;
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

public class MediaCollectionServiceTests
{
    private readonly Mock<IMediaAssetRepository> _mediaAssetRepository = new();
    private readonly Mock<IMediaUploader> _mediaUploader = new();
    private readonly MediaCollectionService _service;

    private readonly Guid _organizationId = Guid.NewGuid();

    public MediaCollectionServiceTests()
    {
        _service = new MediaCollectionService(
            _mediaAssetRepository.Object, _mediaUploader.Object, NullLogger<MediaCollectionService>.Instance);
    }

    private static IFormFile NewFile(string fileName) => NewFile(new MemoryStream([1]), 0, 1, fileName);

    private static IFormFile NewFile(Stream body, long offset, long length, string fileName) =>
        new FormFile(body, offset, length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

    private void SetupUpload(string fileName, Func<Task<MediaAsset>> upload) =>
        _mediaUploader
            .Setup(u => u.UploadAsync(
                It.Is<IFormFile>(f => f.FileName == fileName), "folder", _organizationId, It.IsAny<CancellationToken>()))
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
    public async Task UploadAsync_AllSucceed_ReturnsUntrackedAssetsTaggedWithEntityTypeInFileOrder()
    {
        // "a" finishes last, yet must stay first: order decides sort_order and the cover
        SetupUpload("a.png", async () =>
        {
            await Task.Delay(50);
            return Asset("a");
        });
        SetupUpload("b.png", () => Task.FromResult(Asset("b")));

        var uploaded = await _service.UploadAsync(
            MediaAssetEntityType.RoomMedia, [NewFile("a.png"), NewFile("b.png")], "folder", _organizationId);

        Assert.Equal(["a", "b"], uploaded.Select(m => m.StorageKey));
        Assert.All(uploaded, m =>
        {
            Assert.Equal(MediaAssetEntityType.RoomMedia, m.EntityType);
            Assert.Null(m.EntityId);
        });
        _mediaAssetRepository.VerifyNoOtherCalls();
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
    public async Task UploadAsync_FilesSharingOneRequestBody_NeverReadsTheBodyConcurrently()
    {
        // Mirrors ASP.NET multipart binding: both form files are views over the same buffered body
        var body = new ExclusiveReadStream("aaaabbbb"u8.ToArray());
        var contents = new ConcurrentDictionary<string, string>();
        using var bothStarted = new Barrier(2);
        _mediaUploader
            .Setup(u => u.UploadAsync(It.IsAny<IFormFile>(), "folder", _organizationId, It.IsAny<CancellationToken>()))
            .Returns((IFormFile file, string _, Guid? _, CancellationToken _) => Task.Run(() =>
            {
                bothStarted.SignalAndWait(TimeSpan.FromSeconds(5));   // both uploads read at the same moment
                using var stream = file.OpenReadStream();
                var read = new byte[file.Length];
                stream.ReadExactly(read);
                contents[file.FileName] = Encoding.ASCII.GetString(read);
                return Asset(file.FileName);
            }));

        await _service.UploadAsync(
            MediaAssetEntityType.RoomMedia,
            [NewFile(body, 0, 4, "a.png"), NewFile(body, 4, 4, "b.png")],
            "folder",
            _organizationId);

        Assert.Equal("aaaa", contents["a.png"]);
        Assert.Equal("bbbb", contents["b.png"]);
    }

    [Fact]
    public async Task UploadAsync_OneFileFails_WaitsForOthersDeletesEverySuccessfulUploadAndRethrows()
    {
        SetupUpload("a.png", async () =>
        {
            await Task.Delay(50);   // still running when "b" fails: must be awaited, not abandoned
            return Asset("a");
        });
        SetupUpload("b.png", () => Task.FromException<MediaAsset>(
            new AppValidationException("File content does not match its declared type")));
        SetupUpload("c.png", () => Task.FromResult(Asset("c")));

        await Assert.ThrowsAsync<AppValidationException>(() => _service.UploadAsync(
            MediaAssetEntityType.RoomMedia, [NewFile("a.png"), NewFile("b.png"), NewFile("c.png")], "folder", _organizationId));

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
        _mediaUploader.Verify(u => u.TryDeleteAsync("c", "image/png"), Times.Once);
        _mediaAssetRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StageAddAsync_AddsEveryAsset()
    {
        var a = Asset("a");
        var b = Asset("b");

        await _service.StageAddAsync([a, b]);

        _mediaAssetRepository.Verify(r => r.AddAsync(a, It.IsAny<CancellationToken>()), Times.Once);
        _mediaAssetRepository.Verify(r => r.AddAsync(b, It.IsAny<CancellationToken>()), Times.Once);
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

    [Fact]
    public async Task DiscardAsync_DeletesUploadedFiles()
    {
        await _service.DiscardAsync([Asset("a")]);

        _mediaUploader.Verify(u => u.TryDeleteAsync("a", "image/png"), Times.Once);
    }
}
