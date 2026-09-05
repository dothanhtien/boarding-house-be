using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

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
    }
}
