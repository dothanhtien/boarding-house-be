using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class RoomMediaConfiguration : IEntityTypeConfiguration<RoomMedia>
{
    public const string RoomIdIndex = "ix_room_media_room_id";
    public const string MediaAssetIdUniqueIndex = "ix_room_media_media_asset_id";
    public const string RoomIdCoverUniqueIndex = "ix_room_media_room_id_cover";

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
