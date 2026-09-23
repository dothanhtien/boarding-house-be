using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public const string PropertyIdRoomNumberUniqueIndex = "ix_rooms_property_id_room_number";

    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.HasIndex(r => r.PropertyId);

        builder.HasIndex(r => r.RoomStatus);

        builder.HasIndex(r => new { r.PropertyId, r.RoomNumber })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(PropertyIdRoomNumberUniqueIndex);

        builder.Property(r => r.RoomNumber).HasMaxLength(20);

        builder.Property(r => r.RoomCategory)
            .HasConversion(RoomCategoryDbValueConverter.Instance)
            .HasMaxLength(20)
            .HasDefaultValue(RoomCategory.Standard);

        builder.Property(r => r.RoomStatus)
            .HasConversion(RoomStatusDbValueConverter.Instance)
            .HasMaxLength(20)
            .HasDefaultValue(RoomStatus.Available);

        builder.Property(r => r.Area).HasColumnType("decimal(10,2)");

        builder.Property(r => r.MonthlyRent).HasColumnType("decimal(18,2)");

        builder.Property(r => r.DepositAmount).HasColumnType("decimal(18,2)");

        builder.HasOne(r => r.Property)
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
