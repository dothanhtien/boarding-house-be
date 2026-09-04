using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.HasIndex(r => r.UserId);
        builder.Property(r => r.RevokedReason)
            .HasConversion(
                v => v.ToString()!.ToUpperInvariant(),
                v => Enum.Parse<RevokedReason>(v, ignoreCase: true))
            .HasMaxLength(50);
        builder.Property(r => r.IpAddress).HasMaxLength(45);

        builder.HasOne(r => r.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
