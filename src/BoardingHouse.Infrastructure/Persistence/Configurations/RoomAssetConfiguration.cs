using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class RoomAssetConfiguration : IEntityTypeConfiguration<RoomAsset>
{
    public void Configure(EntityTypeBuilder<RoomAsset> builder)
    {
        builder.HasIndex(a => a.RoomId);

        builder.Property(a => a.Name).HasMaxLength(100);

        builder.Property(a => a.PurchaseUnitPrice).HasColumnType("decimal(18,2)");

        builder.Property(a => a.Condition)
            .HasConversion(AssetConditionDbValueConverter.Instance)
            .HasMaxLength(20)
            .HasDefaultValue(AssetCondition.Good);

        builder.HasOne(a => a.Room)
            .WithMany(r => r.Assets)
            .HasForeignKey(a => a.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RoomAsset>()
            .WithMany()
            .HasForeignKey(a => a.SplitFromAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
