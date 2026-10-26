namespace BusinessLayer.Features.Enrollments.Get;

public sealed class GetEnrollmentCommand
{
    public required long UserId { get; init; }

    public required long EnrollmentId { get; init; }
}
