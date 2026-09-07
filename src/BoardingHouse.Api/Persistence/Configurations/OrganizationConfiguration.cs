using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.HasIndex(o => o.Name);

        builder.Property(o => o.Name).HasMaxLength(255);
        builder.Property(o => o.TaxCode).HasMaxLength(20);
        builder.Property(o => o.Phone).HasMaxLength(20);
        builder.Property(o => o.Email).HasMaxLength(255);
        builder.Property(o => o.Province).HasMaxLength(100);
        builder.Property(o => o.District).HasMaxLength(100);
        builder.Property(o => o.Ward).HasMaxLength(100);
    }
}
