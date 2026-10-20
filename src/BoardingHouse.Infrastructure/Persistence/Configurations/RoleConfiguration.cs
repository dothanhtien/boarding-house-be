using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Infrastructure.Persistence.Converters;

namespace BoardingHouse.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasIndex(r => r.Name).IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(r => r.Slug).IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.Slug).HasMaxLength(100);

        builder.Property(r => r.Scope)
            .HasConversion(RoleScopeDbValueConverter.Instance)
            .HasMaxLength(20);
    }
}
