using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;
using BoardingHouse.Application.Persistence;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.HasIndex(m => m.OrganizationId);

        builder
            .HasIndex(m => new { m.EntityType, m.EntityId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(UniqueIndexNames.MediaAssetEntityTypeEntityId);

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
