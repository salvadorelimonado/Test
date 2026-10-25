using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccessLayer.Configurations;

public class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired();

        builder
            .HasOne(x => x.Room)
            .WithMany()
            .HasForeignKey(x => x.RoomId);

        builder.HasIndex(x => new { x.StartsAt, x.Id })
            .HasFilter("is_cancelled = false");
    }
}
