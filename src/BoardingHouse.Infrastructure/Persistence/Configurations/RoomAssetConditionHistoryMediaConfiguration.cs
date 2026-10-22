using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class RoomAssetConditionHistoryMediaConfiguration : IEntityTypeConfiguration<RoomAssetConditionHistoryMedia>
{
    private const string ConditionHistoryIdIndex = "ix_room_asset_condition_history_media_condition_history_id";
    private const string MediaAssetIdUniqueIndex = "ix_room_asset_condition_history_media_media_asset_id";

    public void Configure(EntityTypeBuilder<RoomAssetConditionHistoryMedia> builder)
    {
        builder.HasIndex(m => m.ConditionHistoryId, ConditionHistoryIdIndex)
            .HasDatabaseName(ConditionHistoryIdIndex);

        builder.HasIndex(m => m.MediaAssetId, MediaAssetIdUniqueIndex)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(MediaAssetIdUniqueIndex);

        builder.HasOne(m => m.ConditionHistory)
            .WithMany(h => h.Media)
            .HasForeignKey(m => m.ConditionHistoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.MediaAsset)
            .WithMany()
            .HasForeignKey(m => m.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
