using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public interface IMediaAttachmentService
{
    Task<MediaAttachmentChange> StageReplaceAsync(
        MediaAssetEntityType entityType,
        Guid entityId,
        IFileUpload file,
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
