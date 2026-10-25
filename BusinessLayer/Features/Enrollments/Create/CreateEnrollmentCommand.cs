namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreateEnrollmentCommand
{
    public required long UserId { get; init; }

    public required long ClassId { get; init; }

    public required int Seats { get; init; }

    public required string IdempotencyKey { get; init; }
}
