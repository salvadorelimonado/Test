namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelEnrollmentCommand
{
    public required long UserId { get; init; }

    public required long EnrollmentId { get; init; }
}
