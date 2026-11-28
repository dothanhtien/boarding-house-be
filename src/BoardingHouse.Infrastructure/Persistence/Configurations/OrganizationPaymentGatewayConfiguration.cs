using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class OrganizationPaymentGatewayConfiguration : IEntityTypeConfiguration<OrganizationPaymentGateway>
{
    public void Configure(EntityTypeBuilder<OrganizationPaymentGateway> builder)
    {
        builder.HasIndex(g => new { g.OrganizationId, g.Gateway })
            .IsUnique()
            .HasDatabaseName(UniqueIndexNames.OrganizationPaymentGatewayOrganizationIdGateway);

        builder.Property(g => g.Gateway)
            .HasConversion(PaymentGatewayDbValueConverter.Instance)
            .HasMaxLength(20);

        builder.Property(g => g.MerchantCode).HasMaxLength(OrganizationPaymentGateway.MerchantCodeMaxLength);
        builder.Property(g => g.SecretEncrypted).HasColumnType("text");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(g => g.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
