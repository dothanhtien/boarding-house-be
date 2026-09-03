using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasIndex(u => u.Email).IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(u => u.Phone).IsUnique()
            .HasFilter("phone IS NOT NULL AND deleted_at IS NULL");
        builder.HasIndex(u => u.IsActive);

        builder.Property(u => u.Email).HasMaxLength(255);
        builder.Property(u => u.Phone).HasMaxLength(20);
        builder.Property(u => u.FullName).HasMaxLength(255);
    }
}
