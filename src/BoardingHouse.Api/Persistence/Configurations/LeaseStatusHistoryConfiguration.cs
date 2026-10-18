using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Api.Persistence.Converters;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Persistence.Configurations;

public class LeaseStatusHistoryConfiguration : IEntityTypeConfiguration<LeaseStatusHistory>
{
    public void Configure(EntityTypeBuilder<LeaseStatusHistory> builder)
    {
        builder.HasIndex(h => new { h.LeaseId, h.ChangedAt });

        builder.Property(h => h.OldStatus)
            .HasConversion(LeaseStatusDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(h => h.NewStatus)
            .HasConversion(LeaseStatusDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.HasOne(h => h.Lease)
            .WithMany()
            .HasForeignKey(h => h.LeaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(h => h.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
