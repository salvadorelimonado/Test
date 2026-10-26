namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistCommand
{
    public required long UserId { get; init; }

    public required long ClassId { get; init; }

    public required int Seats { get; init; }

    public required string IdempotencyKey { get; init; }
}
