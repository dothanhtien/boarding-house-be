using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class TenantProfileConfiguration : IEntityTypeConfiguration<TenantProfile>
{
    public const string UserIdUniqueIndex = "ix_tenant_profiles_user_id";
    public const string IdentityNumberUniqueIndex = "ix_tenant_profiles_identity_number";

    public void Configure(EntityTypeBuilder<TenantProfile> builder)
    {
        builder.HasIndex(t => t.UserId)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(UserIdUniqueIndex);

        builder.HasIndex(t => t.IdentityNumber)
            .IsUnique()
            .HasFilter("identity_number IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName(IdentityNumberUniqueIndex);

        builder.Property(t => t.IdentityType)
            .HasConversion(IdentityTypeDbValueConverter.Instance)
            .HasMaxLength(30);

        builder.Property(t => t.IdentityNumber).HasMaxLength(20);
        builder.Property(t => t.IdentityIssuedPlace).HasMaxLength(100);
        builder.Property(t => t.PermanentAddress).HasMaxLength(255);

        builder.Property(t => t.Gender)
            .HasConversion(GenderDbValueConverter.Instance)
            .HasMaxLength(10);

        builder.Property(t => t.EmergencyContactName).HasMaxLength(100);
        builder.Property(t => t.EmergencyContactPhone).HasMaxLength(20);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_tenant_profiles_identity_pair",
                "(identity_type IS NULL) = (identity_number IS NULL)");
            t.HasCheckConstraint(
                "ck_tenant_profiles_identity_details",
                "identity_number IS NOT NULL OR (identity_issued_date IS NULL AND identity_issued_place IS NULL AND identity_expiry_date IS NULL)");
            t.HasCheckConstraint(
                "ck_tenant_profiles_identity_dates",
                "identity_issued_date IS NULL OR identity_expiry_date IS NULL OR identity_expiry_date > identity_issued_date");
        });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
