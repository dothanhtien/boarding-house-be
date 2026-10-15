using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public const string OrganizationIdLeaseCodeUniqueIndex = "ix_leases_organization_id_lease_code";
    public const string RoomIdActiveUniqueIndex = "ix_leases_room_id_active";
    public const string PreviousLeaseIdUniqueIndex = "ix_leases_previous_lease_id";

    public void Configure(EntityTypeBuilder<Lease> builder)
    {
        builder.HasIndex(l => new { l.OrganizationId, l.LeaseCode })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(OrganizationIdLeaseCodeUniqueIndex);

        builder.HasIndex(l => l.RoomId, RoomIdActiveUniqueIndex)
            .IsUnique()
            .HasFilter("status = 'ACTIVE' AND deleted_at IS NULL")
            .HasDatabaseName(RoomIdActiveUniqueIndex);

        builder.HasIndex(l => l.RoomId, "ix_leases_room_id")
            .HasDatabaseName("ix_leases_room_id");

        builder.HasIndex(l => l.PreviousLeaseId)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(PreviousLeaseIdUniqueIndex);

        builder.HasIndex(l => l.Status);

        builder.Property(l => l.LeaseCode).HasMaxLength(50);

        builder.Property(l => l.MonthlyRent).HasColumnType("decimal(18,2)");

        builder.Property(l => l.DepositAmount).HasColumnType("decimal(18,2)");

        builder.Property(l => l.Status)
            .HasConversion(LeaseStatusDbValueConverter.Instance)
            .HasMaxLength(20)
            .HasDefaultValue(LeaseStatus.Draft);

        builder.HasOne(l => l.Organization)
            .WithMany()
            .HasForeignKey(l => l.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Room)
            .WithMany()
            .HasForeignKey(l => l.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.PreviousLease)
            .WithMany()
            .HasForeignKey(l => l.PreviousLeaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_leases_end_date", "end_date IS NULL OR end_date > start_date");
            t.HasCheckConstraint("ck_leases_billing_day", "billing_day IS NULL OR (billing_day BETWEEN 1 AND 28)");
            t.HasCheckConstraint("ck_leases_monthly_rent", "monthly_rent > 0");
            t.HasCheckConstraint("ck_leases_deposit_amount", "deposit_amount >= 0");
            t.HasCheckConstraint("ck_leases_previous_lease", "previous_lease_id IS NULL OR previous_lease_id <> id");
        });
    }
}
