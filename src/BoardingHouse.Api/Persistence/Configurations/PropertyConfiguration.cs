using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.HasIndex(p => p.OrganizationId);

        builder
            .HasIndex(p => new { p.OrganizationId, p.Name })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.Property(p => p.Name).HasMaxLength(100);

        builder.Property(p => p.Province).HasMaxLength(100);

        builder.Property(p => p.District).HasMaxLength(100);

        builder.Property(p => p.Ward).HasMaxLength(100);

        builder.Property(p => p.LateFeeType)
            .HasConversion(LateFeeTypeDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(p => p.LateFeeValue).HasColumnType("decimal(18,2)");

        builder.HasOne(p => p.Organization)
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Properties_DefaultBillingDay",
                "default_billing_day IS NULL OR (default_billing_day BETWEEN 1 AND 28)");
            t.HasCheckConstraint(
                "CK_Properties_LateFee",
                "(late_fee_type IS NULL) = (late_fee_value IS NULL)");
        });
    }
}
