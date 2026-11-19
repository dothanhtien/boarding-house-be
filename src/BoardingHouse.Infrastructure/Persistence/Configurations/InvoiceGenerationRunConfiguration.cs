using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class InvoiceGenerationRunConfiguration : IEntityTypeConfiguration<InvoiceGenerationRun>
{
    public void Configure(EntityTypeBuilder<InvoiceGenerationRun> builder)
    {
        builder.HasIndex(r => new { r.OrganizationId, r.BillingMonth });

        builder.Property(r => r.BillingMonth).HasConversion(BillingMonthDbValueConverter.Instance);

        builder.Property(r => r.Skipped)
            .HasConversion(InvoiceGenerationSkipsDbValueConverter.Instance, InvoiceGenerationSkipsDbValueConverter.Comparer)
            .HasColumnType("jsonb");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.TriggeredBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_invoice_generation_runs_billing_month", "EXTRACT(DAY FROM billing_month) = 1");
            t.HasCheckConstraint("ck_invoice_generation_runs_created_count", "created_count >= 0");
            t.HasCheckConstraint("ck_invoice_generation_runs_finished_at", "finished_at >= started_at");
        });
    }
}
