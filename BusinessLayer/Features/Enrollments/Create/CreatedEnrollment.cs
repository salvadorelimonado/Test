using DataAccessLayer.Entities;

namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreatedEnrollment
{
    public long Id { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public EnrollmentStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
