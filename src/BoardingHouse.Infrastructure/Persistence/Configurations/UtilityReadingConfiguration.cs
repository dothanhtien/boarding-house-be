using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Persistence;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class UtilityReadingConfiguration : IEntityTypeConfiguration<UtilityReading>
{
    public void Configure(EntityTypeBuilder<UtilityReading> builder)
    {
        builder.HasIndex(r => new { r.RoomId, r.ReadingMonth, r.ServiceId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(UniqueIndexNames.UtilityReadingRoomIdReadingMonthServiceId);

        builder.HasIndex(r => new { r.RoomId, r.ServiceId, r.ReadOn });

        builder.HasIndex(r => r.BilledLeaseId);

        builder.Property(r => r.PreviousValue).HasColumnType("decimal(18,2)");

        builder.Property(r => r.CurrentValue).HasColumnType("decimal(18,2)");

        builder.Property(r => r.Consumption)
            .HasColumnType("decimal(18,2)")
            .HasComputedColumnSql("current_value - previous_value", stored: true);

        builder.HasOne(r => r.Room)
            .WithMany()
            .HasForeignKey(r => r.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Service)
            .WithMany()
            .HasForeignKey(r => r.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.BilledLease)
            .WithMany()
            .HasForeignKey(r => r.BilledLeaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_utility_readings_reading_month", "EXTRACT(DAY FROM reading_month) = 1");
            t.HasCheckConstraint(
                "ck_utility_readings_read_on",
                "read_on >= reading_month - INTERVAL '1 month' AND read_on < reading_month + INTERVAL '1 month'");
            t.HasCheckConstraint("ck_utility_readings_current_value", "current_value >= 0");
            t.HasCheckConstraint(
                "ck_utility_readings_previous_value",
                "previous_value IS NULL OR (previous_value >= 0 AND previous_value <= current_value)");
        });
    }
}
