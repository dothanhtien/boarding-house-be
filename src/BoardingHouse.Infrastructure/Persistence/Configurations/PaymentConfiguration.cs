using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasIndex(p => p.InvoiceId);

        builder.HasIndex(p => new { p.Status, p.PaymentDate });

        builder.Property(p => p.PayerNameOverride).HasMaxLength(Payment.PayerNameOverrideMaxLength);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.TransactionReference).HasMaxLength(Payment.TransactionReferenceMaxLength);

        builder.Property(p => p.Method)
            .HasColumnName("payment_method")
            .HasConversion(PaymentMethodDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(p => p.Status)
            .HasConversion(PaymentStatusDbValueConverter.Instance)
            .HasMaxLength(20)
            .HasDefaultValue(PaymentStatus.Pending);

        builder.HasOne(p => p.Invoice)
            .WithMany()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PayerUser)
            .WithMany()
            .HasForeignKey(p => p.PayerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.ConfirmedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.VoidedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            t.HasCheckConstraint("ck_payments_target", "(invoice_id IS NULL) <> (deposit_return_id IS NULL)");
            t.HasCheckConstraint("ck_payments_payer", "payer_user_id IS NULL OR payer_name_override IS NULL");
            t.HasCheckConstraint("ck_payments_confirmed", "status <> 'CONFIRMED' OR confirmed_at IS NOT NULL");
            t.HasCheckConstraint(
                "ck_payments_voided",
                "status <> 'VOIDED' OR (voided_at IS NOT NULL AND void_reason IS NOT NULL)");
        });
    }
}
