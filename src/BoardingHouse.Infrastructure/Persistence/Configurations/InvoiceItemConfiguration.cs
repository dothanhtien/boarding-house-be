using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.HasIndex(i => new { i.SourceType, i.SourceId });

        builder.Property(i => i.Description).HasMaxLength(InvoiceItem.DescriptionMaxLength);
        builder.Property(i => i.Quantity).HasColumnType("decimal(18,2)");
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.Amount).HasColumnType("decimal(18,2)");

        builder.Property(i => i.SourceType)
            .HasConversion(InvoiceItemSourceTypeDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_invoice_items_quantity", "quantity >= 0");
            t.HasCheckConstraint("ck_invoice_items_source", "(source_type = 'CUSTOM') = (source_id IS NULL)");
        });
    }
}
