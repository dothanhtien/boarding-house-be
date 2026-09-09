using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.HasKey(s => s.OrganizationId);

        builder.Property(s => s.LateFeeValue).HasColumnType("decimal(18,2)");
        builder.Property(s => s.VatRate).HasColumnType("decimal(5,2)");
        builder.Property(s => s.Currency).HasMaxLength(3).HasDefaultValue("VND");
        builder.Property(s => s.BankAccountNumber).HasMaxLength(50);
        builder.Property(s => s.BankName).HasMaxLength(100);
        builder.Property(s => s.BankAccountName).HasMaxLength(100);

        builder.Property(s => s.LateFeeType)
            .HasConversion(LateFeeTypeDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_organization_settings_default_billing_day",
                "default_billing_day IS NULL OR (default_billing_day BETWEEN 1 AND 28)");
            t.HasCheckConstraint(
                "ck_organization_settings_late_fee_pair",
                "(late_fee_type IS NULL) = (late_fee_value IS NULL)");
        });

        builder.HasOne(s => s.Organization)
            .WithOne()
            .HasForeignKey<OrganizationSettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => s.Organization!.DeletedAt == null);
    }
}
