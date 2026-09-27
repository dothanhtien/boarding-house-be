using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Storage;

namespace BoardingHouse.Api.Services;

public class MediaAttachmentService(
    IMediaAssetRepository mediaAssetRepository,
    IMediaUploader mediaUploader,
    ILogger<MediaAttachmentService> logger) : IMediaAttachmentService
{
    public async Task<MediaAttachmentChange> StageReplaceAsync(
        MediaAssetEntityType entityType,
        Guid entityId,
        IFormFile file,
        string folder,
        Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        var added = await mediaUploader.UploadAsync(file, folder, organizationId, cancellationToken);
        added.EntityType = entityType;
        added.EntityId = entityId;

        try
        {
            var removed = await mediaAssetRepository.GetByEntityAsync(entityType, entityId, cancellationToken);
            if (removed is not null)
            {
                mediaAssetRepository.SoftDelete(removed);
            }

            await mediaAssetRepository.AddAsync(added, cancellationToken);

            return new MediaAttachmentChange(added, removed);
        }
        catch
        {
            await mediaUploader.TryDeleteAsync(added.StorageKey, added.MimeType);
            throw;
        }
    }

    public async Task<MediaAttachmentChange> StageRemoveAsync(
        MediaAssetEntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var removed = await mediaAssetRepository.GetByEntityAsync(entityType, entityId, cancellationToken);
        if (removed is null)
        {
            return MediaAttachmentChange.None;
        }

        mediaAssetRepository.SoftDelete(removed);
        return new MediaAttachmentChange(null, removed);
    }

    public async Task CompleteAsync(MediaAttachmentChange change)
    {
        if (change.Added is { } added)
        {
            logger.LogInformation(
                "Media attachment set ({EntityType}, {EntityId}, {MediaAssetId})", added.EntityType, added.EntityId, added.Id);
        }

        if (change.Removed is { } removed)
        {
            logger.LogInformation(
                "Media attachment removed ({EntityType}, {EntityId}, {MediaAssetId})", removed.EntityType, removed.EntityId, removed.Id);
            await mediaUploader.TryDeleteAsync(removed.StorageKey, removed.MimeType);
        }
    }

    public async Task DiscardAsync(MediaAttachmentChange change)
    {
        if (change.Added is { } added)
        {
            await mediaUploader.TryDeleteAsync(added.StorageKey, added.MimeType);
        }
    }
}
