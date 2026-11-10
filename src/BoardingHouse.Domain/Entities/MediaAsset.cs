using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class MediaAsset : BaseEntity
{
    public Guid? OrganizationId { get; private set; }   // NULL = platform-level asset (e.g. user avatars)
    public Organization? Organization { get; private set; }

    public string FileName { get; private set; } = null!;
    public StorageProvider StorageProvider { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string FileUrl { get; private set; } = null!;
    public string MimeType { get; private set; } = null!;
    public long FileSize { get; private set; }

    public MediaAssetEntityType? EntityType { get; private set; }   // Purpose of the asset; NULL = purpose not set yet
    public Guid? EntityId { get; private set; }                     // Single-slot types only; NULL for multi-asset types (linked via a join table)

    public Guid? UploadedByUserId { get; private set; }
    public User? UploadedByUser { get; private set; }

    private MediaAsset() { }

    public static MediaAsset Uploaded(
        Guid? organizationId,
        string fileName,
        StorageProvider storageProvider,
        string storageKey,
        string fileUrl,
        string mimeType,
        long fileSize,
        Guid uploadedBy) => new()
        {
            OrganizationId = organizationId,
            FileName = fileName,
            StorageProvider = storageProvider,
            StorageKey = storageKey,
            FileUrl = fileUrl,
            MimeType = mimeType,
            FileSize = fileSize,
            UploadedByUserId = uploadedBy,
            CreatedBy = uploadedBy
        };

    public void AssignTo(MediaAssetEntityType entityType, Guid? entityId = null)
    {
        if (EntityType is not null)
        {
            throw new InvalidOperationException($"Media asset '{Id}' is already assigned to {EntityType}");
        }

        EntityType = entityType;
        EntityId = entityId;
    }
}
