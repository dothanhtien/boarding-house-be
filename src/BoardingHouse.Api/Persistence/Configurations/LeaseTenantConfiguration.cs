using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class LeaseTenantConfiguration : IEntityTypeConfiguration<LeaseTenant>
{
    public const string LeaseIdUserIdUniqueIndex = "ix_lease_tenants_lease_id_user_id";
    public const string LeaseIdRepresentativeUniqueIndex = "ix_lease_tenants_lease_id_representative";

    public void Configure(EntityTypeBuilder<LeaseTenant> builder)
    {
        builder.HasIndex(t => new { t.LeaseId, t.UserId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(LeaseIdUserIdUniqueIndex);

        builder.HasIndex(t => t.LeaseId)
            .IsUnique()
            .HasFilter("is_representative AND deleted_at IS NULL")
            .HasDatabaseName(LeaseIdRepresentativeUniqueIndex);

        builder.HasIndex(t => t.UserId);

        builder.HasOne(t => t.Lease)
            .WithMany(l => l.Tenants)
            .HasForeignKey(t => t.LeaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
            t.HasCheckConstraint(
                "ck_lease_tenants_left_date",
                "left_date IS NULL OR joined_date IS NULL OR left_date >= joined_date"));
    }
}
