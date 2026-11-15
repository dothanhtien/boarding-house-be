using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class InvoiceNumberSequenceConfiguration : IEntityTypeConfiguration<InvoiceNumberSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSequence> builder)
    {
        builder.HasKey(s => new { s.OrganizationId, s.BillingMonth });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_invoice_number_sequences_billing_month", "EXTRACT(DAY FROM billing_month) = 1");
            t.HasCheckConstraint("ck_invoice_number_sequences_last_number", "last_number > 0");
        });
    }
}
