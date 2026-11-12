using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasIndex(i => new { i.OrganizationId, i.InvoiceNumber })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(UniqueIndexNames.InvoiceOrganizationIdInvoiceNumber);

        builder.HasIndex(i => new { i.LeaseId, i.BillingMonth })
            .IsUnique()
            .HasFilter("deleted_at IS NULL AND status <> 'CANCELLED'")
            .HasDatabaseName(UniqueIndexNames.InvoiceLeaseIdBillingMonth);

        builder.HasIndex(i => new { i.OrganizationId, i.BillingMonth });

        builder.HasIndex(i => i.Status);

        builder.Property(i => i.InvoiceNumber).HasMaxLength(Invoice.InvoiceNumberMaxLength);
        builder.Property(i => i.BillingMonth).HasConversion(BillingMonthDbValueConverter.Instance);
        builder.Property(i => i.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(i => i.VatRate).HasColumnType("decimal(5,2)");
        builder.Property(i => i.VatAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.LateFeeAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.DiscountReason).HasMaxLength(Invoice.DiscountReasonMaxLength);
        builder.Property(i => i.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.PaidAmount).HasColumnType("decimal(18,2)");

        builder.Property(i => i.Status)
            .HasConversion(InvoiceStatusDbValueConverter.Instance)
            .HasMaxLength(20)
            .HasDefaultValue(InvoiceStatus.Pending);

        builder.HasOne(i => i.Organization)
            .WithMany()
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Lease)
            .WithMany()
            .HasForeignKey(i => i.LeaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Items)
            .WithOne(item => item.Invoice)
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_invoices_billing_month", "EXTRACT(DAY FROM billing_month) = 1");
            t.HasCheckConstraint("ck_invoices_vat_rate", "vat_rate >= 0 AND vat_rate <= 100");
            t.HasCheckConstraint(
                "ck_invoices_discount",
                "discount_amount >= 0 AND (discount_amount = 0 OR discount_reason IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_invoices_amounts",
                "vat_amount >= 0 AND late_fee_amount >= 0 AND paid_amount >= 0 AND total_amount >= 0");
            t.HasCheckConstraint(
                "ck_invoices_total_amount",
                "total_amount = subtotal + vat_amount + late_fee_amount - discount_amount");
        });
    }
}
