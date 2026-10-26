namespace BusinessLayer.Features.Enrollments.Get;

public sealed class GetEnrollmentQuery
{
    public required long UserId { get; init; }

    public required long EnrollmentId { get; init; }
}
