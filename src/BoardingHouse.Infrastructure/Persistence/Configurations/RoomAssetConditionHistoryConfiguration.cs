using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class RoomAssetConditionHistoryConfiguration : IEntityTypeConfiguration<RoomAssetConditionHistory>
{
    public void Configure(EntityTypeBuilder<RoomAssetConditionHistory> builder)
    {
        builder.HasIndex(h => new { h.AssetId, h.ChangedAt, h.Id });

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
