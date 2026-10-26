using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccessLayer.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Operation).HasConversion<string>().IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(IdempotencyRecord.HashLength).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(IdempotencyRecord.HashLength).IsRequired();
        builder.Property(x => x.ResponseBody).IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId);

        builder.HasIndex(x => new { x.UserId, x.Operation, x.KeyHash })
            .IsUnique()
            .HasDatabaseName(IdempotencyRecord.KeyIndexName);

        builder.HasIndex(x => x.CreatedAt);
    }
}
