using DataAccessLayer.Database.Converters;
using DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccessLayer.Configurations;

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RefundedVisits).HasDefaultValue(0);

        builder.Property(x => x.Status).HasConversion(LowercaseEnumConverter.Create<EnrollmentStatus>()).IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId);

        builder
            .HasOne(x => x.Class)
            .WithMany(x => x.Enrollments)
            .HasForeignKey(x => x.ClassId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ClassId, x.Status })
            .IncludeProperties(x => x.Seats);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_enrollments_seats",
            "seats > 0"));
    }
}
