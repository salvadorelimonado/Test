using DataAccessLayer.Entities;

namespace WebApi.Features.Enrollments.Cancel;

public sealed class CancelEnrollmentResponse
{
    public required long Id { get; init; }

    public required EnrollmentStatus Status { get; init; }

    public required int RefundedVisits { get; init; }
}
