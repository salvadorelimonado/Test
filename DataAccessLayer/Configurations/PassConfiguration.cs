using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccessLayer.Configurations;

public class PassConfiguration : IEntityTypeConfiguration<Pass>
{
    public void Configure(EntityTypeBuilder<Pass> builder)
    {
        builder.HasKey(x => x.Id);

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId);
    }
}
