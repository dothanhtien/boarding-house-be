using BoardingHouse.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardingHouse.Api.Persistence.Configurations;

public class RoomAmenityConfiguration : IEntityTypeConfiguration<RoomAmenity>
{
    public void Configure(EntityTypeBuilder<RoomAmenity> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(100);

        builder.Property(a => a.Icon).HasMaxLength(50);

        builder.HasOne(a => a.Room)
            .WithMany(r => r.Amenities)
            .HasForeignKey(a => a.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
