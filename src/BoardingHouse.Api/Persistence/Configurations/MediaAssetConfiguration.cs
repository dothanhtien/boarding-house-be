using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public const string EntityTypeEntityIdUniqueIndex = "ix_media_assets_entity_type_entity_id";

    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.HasIndex(m => m.OrganizationId);

        builder
            .HasIndex(m => new { m.EntityType, m.EntityId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.Property(m => m.FileName).HasMaxLength(255);

        builder.Property(m => m.StorageProvider)
            .HasConversion(StorageProviderDbValueConverter.Instance)
            .HasMaxLength(30);

        builder.Property(m => m.MimeType).HasMaxLength(100);

        builder.Property(m => m.EntityType)
            .HasConversion(MediaAssetEntityTypeDbValueConverter.Instance)
            .HasMaxLength(30);

        builder.HasOne(m => m.Organization)
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.UploadedByUser)
            .WithMany()
            .HasForeignKey(m => m.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_MediaAssets_FileSize", "file_size >= 0");
            t.HasCheckConstraint(
                "CK_MediaAssets_EntityLink",
                "entity_id IS NULL OR entity_type IS NOT NULL");
        });
    }
}
