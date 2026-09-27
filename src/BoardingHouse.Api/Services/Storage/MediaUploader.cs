using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;

namespace BoardingHouse.Api.Services.Storage;

public class MediaUploader(
    IStorageProvider storageProvider,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<MediaUploader> logger) : IMediaUploader
{
    public async Task<MediaAsset> UploadAsync(
        IFormFile file, string folder, Guid? organizationId, CancellationToken cancellationToken = default)
    {
        await using var content = file.OpenReadStream();

        var fileName = Path.GetFileName(file.FileName);
        var mimeType = await DetectMimeTypeAsync(content, cancellationToken);
        if (mimeType is null || !mimeType.Equals(file.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Upload rejected: content does not match declared type ({FileName}, {DeclaredMimeType}, {DetectedMimeType})",
                fileName, file.ContentType, mimeType);
            throw new AppValidationException("File content does not match its declared type");
        }

        var stored = await storageProvider.UploadAsync(content, fileName, mimeType, folder, cancellationToken);

        var userId = currentUserAccessor.RequiredUser.Id;
        return new MediaAsset
        {
            OrganizationId = organizationId,
            FileName = fileName,
            StorageProvider = storageProvider.Provider,
            StorageKey = stored.StorageKey,
            FileUrl = stored.Url,
            MimeType = mimeType,
            FileSize = stored.SizeBytes,
            UploadedByUserId = userId,
            CreatedBy = userId
        };
    }

    public async Task TryDeleteAsync(string storageKey, string mimeType)
    {
        try
        {
            await storageProvider.DeleteAsync(storageKey, mimeType, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete file from storage — left orphaned ({StorageKey})", storageKey);
        }
    }

    private static async Task<string?> DetectMimeTypeAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[MediaSignatures.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        content.Position = 0;
        return MediaSignatures.DetectMimeType(header.AsSpan(0, read));
    }
}
