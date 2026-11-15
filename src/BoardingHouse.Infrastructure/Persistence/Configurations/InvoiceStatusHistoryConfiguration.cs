using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class InvoiceStatusHistoryConfiguration : IEntityTypeConfiguration<InvoiceStatusHistory>
{
    public void Configure(EntityTypeBuilder<InvoiceStatusHistory> builder)
    {
        builder.HasIndex(h => new { h.InvoiceId, h.ChangedAt });

        builder.Property(h => h.OldStatus)
            .HasConversion(InvoiceStatusDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(h => h.NewStatus)
            .HasConversion(InvoiceStatusDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.HasOne(h => h.Invoice)
            .WithMany(i => i.StatusHistories)
            .HasForeignKey(h => h.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(h => h.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
