using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Entities;

public class MediaAsset : BaseEntity
{
    public Guid? OrganizationId { get; set; }   // NULL = platform-level asset (e.g. user avatars)
    public Organization? Organization { get; set; }

    public required string FileName { get; set; }
    public StorageProvider StorageProvider { get; set; }
    public required string StorageKey { get; set; }
    public required string FileUrl { get; set; }
    public required string MimeType { get; set; }
    public long FileSize { get; set; }

    public MediaAssetEntityType? EntityType { get; set; }   // Purpose of the asset; NULL = purpose not set yet
    public Guid? EntityId { get; set; }                     // Single-slot types only; NULL for multi-asset types (linked via a join table)

    public Guid? UploadedByUserId { get; set; }
    public User? UploadedByUser { get; set; }
}
