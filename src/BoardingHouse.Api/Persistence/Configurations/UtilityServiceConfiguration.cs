using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class UtilityServiceConfiguration : IEntityTypeConfiguration<UtilityService>
{
    public const string PropertyIdNameUniqueIndex = "ix_utility_services_property_id_name";

    public void Configure(EntityTypeBuilder<UtilityService> builder)
    {
        builder.HasIndex(s => s.PropertyId);

        builder.HasIndex(s => new { s.PropertyId, s.Name })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(PropertyIdNameUniqueIndex);

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
