namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelEnrollmentResponse
{
    public required long Id { get; init; }

    public required string Status { get; init; }

    public required int RefundedVisits { get; init; }
}
