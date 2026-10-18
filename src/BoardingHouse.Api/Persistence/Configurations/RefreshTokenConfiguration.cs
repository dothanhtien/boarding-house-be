using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BoardingHouse.Api.Persistence.Converters;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.HasIndex(r => r.UserId);
        builder.Property(r => r.RevokedReason)
            .HasConversion(RevokedReasonDbValueConverter.Instance)
            .HasMaxLength(50);
        builder.Property(r => r.IpAddress).HasMaxLength(45);

        builder.HasOne(r => r.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(r => r.User!.DeletedAt == null);
    }
}
