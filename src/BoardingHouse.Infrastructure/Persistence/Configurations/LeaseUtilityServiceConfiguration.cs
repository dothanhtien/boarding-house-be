using BoardingHouse.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class LeaseUtilityServiceConfiguration : IEntityTypeConfiguration<LeaseUtilityService>
{
    public void Configure(EntityTypeBuilder<LeaseUtilityService> builder)
    {
        builder.HasKey(s => new { s.LeaseId, s.ServiceId });

        builder.HasIndex(s => s.ServiceId);

        builder.Property(s => s.UnitPriceOverride).HasColumnType("decimal(18,2)");

        builder.Property(s => s.Quantity).HasDefaultValue(1);

        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasOne(s => s.Lease)
            .WithMany(l => l.UtilityServices)
            .HasForeignKey(s => s.LeaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Service)
            .WithMany()
            .HasForeignKey(s => s.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_lease_utility_services_unit_price_override",
                "unit_price_override IS NULL OR unit_price_override >= 0");
            t.HasCheckConstraint("ck_lease_utility_services_quantity", "quantity > 0");
        });
    }
}
