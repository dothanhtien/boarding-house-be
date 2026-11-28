using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class OrganizationPaymentGatewayCredentialHistoryConfiguration
    : IEntityTypeConfiguration<OrganizationPaymentGatewayCredentialHistory>
{
    public void Configure(EntityTypeBuilder<OrganizationPaymentGatewayCredentialHistory> builder)
    {
        builder.HasIndex(h => new { h.OrganizationPaymentGatewayId, h.Version })
            .IsUnique()
            .HasDatabaseName(UniqueIndexNames.OrganizationPaymentGatewayCredentialHistoryGatewayIdVersion);

        builder.Property(h => h.MerchantCode).HasMaxLength(OrganizationPaymentGateway.MerchantCodeMaxLength);
        builder.Property(h => h.SecretEncrypted).HasColumnType("text");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(h => h.ReplacedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
