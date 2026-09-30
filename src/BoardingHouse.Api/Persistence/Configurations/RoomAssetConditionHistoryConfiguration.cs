using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class RoomAssetConditionHistoryConfiguration : IEntityTypeConfiguration<RoomAssetConditionHistory>
{
    public void Configure(EntityTypeBuilder<RoomAssetConditionHistory> builder)
    {
        builder.HasIndex(h => h.AssetId);

        builder.Property(h => h.OldCondition)
            .HasConversion(AssetConditionDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(h => h.NewCondition)
            .HasConversion(AssetConditionDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.HasOne(h => h.Asset)
            .WithMany()
            .HasForeignKey(h => h.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
