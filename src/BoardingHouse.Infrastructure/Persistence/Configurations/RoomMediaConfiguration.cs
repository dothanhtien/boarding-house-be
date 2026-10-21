using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class RoomMediaConfiguration : IEntityTypeConfiguration<RoomMedia>
{
    private const string RoomIdIndex = "ix_room_media_room_id";
    private const string MediaAssetIdUniqueIndex = "ix_room_media_media_asset_id";
    private const string RoomIdCoverUniqueIndex = "ix_room_media_room_id_cover";

    public void Configure(EntityTypeBuilder<RoomMedia> builder)
    {
        builder.HasIndex(m => m.RoomId, RoomIdIndex)
            .HasDatabaseName(RoomIdIndex);

        builder.HasIndex(m => m.MediaAssetId, MediaAssetIdUniqueIndex)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(MediaAssetIdUniqueIndex);

        builder.HasIndex(m => m.RoomId, RoomIdCoverUniqueIndex)
            .IsUnique()
            .HasFilter("is_cover AND deleted_at IS NULL")
            .HasDatabaseName(RoomIdCoverUniqueIndex);

        builder.HasOne(m => m.Room)
            .WithMany(r => r.Media)
            .HasForeignKey(m => m.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.MediaAsset)
            .WithMany()
            .HasForeignKey(m => m.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
