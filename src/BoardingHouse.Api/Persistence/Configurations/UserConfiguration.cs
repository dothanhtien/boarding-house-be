using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string EmailUniqueIndex = "ix_users_email";
    public const string PhoneUniqueIndex = "ix_users_phone";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasIndex(u => u.Email).IsUnique()
            .HasFilter("email IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName(EmailUniqueIndex);

        builder.HasIndex(u => u.Phone).IsUnique()
            .HasFilter("phone IS NOT NULL AND phone != '' AND deleted_at IS NULL")
            .HasDatabaseName(PhoneUniqueIndex);

        builder.HasIndex(u => u.IsActive);

        builder.Property(u => u.Email).HasMaxLength(255);
        builder.Property(u => u.Phone).HasMaxLength(20);
        builder.Property(u => u.FullName).HasMaxLength(255);
    }
}
