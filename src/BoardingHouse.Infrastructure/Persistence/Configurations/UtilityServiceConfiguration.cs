using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;
using BoardingHouse.Application.Persistence;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class UtilityServiceConfiguration : IEntityTypeConfiguration<UtilityService>
{
    public void Configure(EntityTypeBuilder<UtilityService> builder)
    {
        builder.HasIndex(s => s.PropertyId);

        builder.HasIndex(s => new { s.PropertyId, s.Name })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(UniqueIndexNames.UtilityServicePropertyIdName);

        builder.Property(s => s.Name).HasMaxLength(100);

        builder.Property(s => s.Type)
            .HasConversion(UtilityTypeDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(s => s.Unit).HasMaxLength(30);

        builder.Property(s => s.DefaultUnitPrice).HasColumnType("decimal(18,2)");

        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasOne(s => s.Property)
            .WithMany()
            .HasForeignKey(s => s.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
