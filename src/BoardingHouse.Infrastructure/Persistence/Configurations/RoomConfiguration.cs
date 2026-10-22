using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence.Converters;
using BoardingHouse.Application.Persistence;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.HasIndex(r => r.PropertyId);

        builder.HasIndex(r => r.RoomStatus);

        builder.HasIndex(r => new { r.PropertyId, r.RoomNumber })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(UniqueIndexNames.RoomPropertyIdRoomNumber);

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
