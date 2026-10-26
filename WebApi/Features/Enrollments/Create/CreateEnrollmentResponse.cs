using DataAccessLayer.Entities;

namespace WebApi.Features.Enrollments.Create;

public sealed class CreateEnrollmentResponse
{
    public long Id { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public EnrollmentStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
