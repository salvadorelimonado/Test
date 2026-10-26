using DataAccessLayer.Database;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccessLayer.Configurations;

public class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion(LowercaseEnumConverter.Create<WaitlistStatus>()).IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId);

        builder
            .HasOne<Class>()
            .WithMany()
            .HasForeignKey(x => x.ClassId);

        builder
            .HasOne(x => x.PromotedEnrollment)
            .WithMany()
            .HasForeignKey(x => x.PromotedEnrollmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.ClassId, x.Status, x.CreatedAt, x.Id });

        builder.HasIndex(x => new { x.UserId, x.ClassId })
            .IsUnique()
            .HasFilter($"status = '{LowercaseEnumConverter.ToColumn(WaitlistStatus.Waiting)}'");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_waitlist_entries_seats",
            "seats > 0"));
    }
}
