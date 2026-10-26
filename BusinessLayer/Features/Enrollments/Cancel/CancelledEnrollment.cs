using DataAccessLayer.Entities;

namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelledEnrollment
{
    public required long Id { get; init; }

    public required EnrollmentStatus Status { get; init; }

    public required int RefundedVisits { get; init; }
}
