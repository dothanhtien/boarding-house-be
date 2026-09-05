using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasIndex(p => new { p.Resource, p.Action }).IsUnique();

        builder.Property(p => p.Resource).HasMaxLength(50);
        builder.Property(p => p.Action).HasMaxLength(50);
    }
}
