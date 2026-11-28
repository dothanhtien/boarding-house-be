using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasIndex(t => new { t.PaymentId, t.CreatedAt });

        builder.HasIndex(t => new { t.Gateway, t.GatewayTransactionId })
            .IsUnique()
            .HasFilter("gateway_transaction_id IS NOT NULL AND status = 'SUCCESS'")
            .HasDatabaseName(UniqueIndexNames.PaymentTransactionGatewayGatewayTransactionId);

        builder.HasIndex(t => t.CreatedAt).HasFilter("needs_review");

        builder.Property(t => t.Gateway)
            .HasConversion(PaymentGatewayDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(t => t.Type)
            .HasConversion(PaymentTransactionTypeDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(t => t.Status)
            .HasConversion(PaymentTransactionStatusDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(t => t.GatewayTransactionId).HasMaxLength(PaymentTransaction.GatewayTransactionIdMaxLength);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.PaymentUrl).HasColumnType("text");
        builder.Property(t => t.RequestPayload).HasColumnType("jsonb");
        builder.Property(t => t.ResponsePayload).HasColumnType("jsonb");
        builder.Property(t => t.WebhookPayload).HasColumnType("jsonb");
        builder.Property(t => t.Error).HasMaxLength(PaymentTransaction.ErrorMaxLength);
        builder.Property(t => t.NeedsReview).HasDefaultValue(false);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(t => t.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_payment_transactions_checkout_payment",
            "type <> 'CHECKOUT' OR payment_id IS NOT NULL"));
    }
}
