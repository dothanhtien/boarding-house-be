using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardingHouse.UnitTests.Services.Storage;

public class MediaUploaderTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8];

    private readonly Mock<IStorageProvider> _storageProvider = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly MediaUploader _uploader;

    private byte[]? _uploadedContent;

    public MediaUploaderTests()
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
            .Callback<Stream, string, string, string, CancellationToken>((s, _, _, _, _) =>
            {
                using var copy = new MemoryStream();
                s.CopyTo(copy);
                _uploadedContent = copy.ToArray();
            })
            .ReturnsAsync(new StoredFile("root/users/x/new", "https://storage.test/new", 12));

        _uploader = new MediaUploader(_storageProvider.Object, _currentUserAccessor.Object, NullLogger<MediaUploader>.Instance);
    }

    private static IFormFile NewFile(byte[] content, string contentType) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", "me.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    public static TheoryData<byte[], string> MatchingFiles => new()
    {
        { PngBytes, "image/png" },
        { JpegBytes, "image/jpeg" },
        { WebpBytes, "image/webp" },
        { PngBytes, "IMAGE/PNG" }
    };

    [Theory]
    [MemberData(nameof(MatchingFiles))]
    public async Task UploadAsync_ContentMatchesDeclaredType_UploadsWholeFileWithDetectedMimeType(byte[] content, string contentType)
    {
        var asset = await _uploader.UploadAsync(NewFile(content, contentType), "users/x", organizationId: null);

        Assert.Equal(contentType.ToLowerInvariant(), asset.MimeType);
        Assert.Equal(content, _uploadedContent);
        _storageProvider.Verify(p => p.UploadAsync(
            It.IsAny<Stream>(), "me.bin", contentType.ToLowerInvariant(), "users/x", It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<byte[], string> MismatchedFiles => new()
    {
        { "not an image at all"u8.ToArray(), "image/png" },
        { JpegBytes, "image/png" },
        { PngBytes, "image/webp" },
        { [], "image/png" },
        { [0x89, 0x50], "image/png" }
    };

    [Theory]
    [MemberData(nameof(MismatchedFiles))]
    public async Task UploadAsync_ContentDoesNotMatchDeclaredType_ThrowsValidationAndUploadsNothing(byte[] content, string contentType)
    {
        await Assert.ThrowsAsync<AppValidationException>(
            () => _uploader.UploadAsync(NewFile(content, contentType), "users/x", organizationId: null));

        _storageProvider.Verify(p => p.UploadAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
