using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Services;

public interface IMediaAttachmentService
{
    Task<MediaAttachmentChange> StageReplaceAsync(
        MediaAssetEntityType entityType,
        Guid entityId,
        IFormFile file,
        string folder,
        Guid? organizationId,
        CancellationToken cancellationToken = default);
    Task<MediaAttachmentChange> StageRemoveAsync(
        MediaAssetEntityType entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task CompleteAsync(MediaAttachmentChange change);
    Task DiscardAsync(MediaAttachmentChange change);
}

public sealed record MediaAttachmentChange(MediaAsset? Added, MediaAsset? Removed)
{
    public static readonly MediaAttachmentChange None = new(null, null);
}
