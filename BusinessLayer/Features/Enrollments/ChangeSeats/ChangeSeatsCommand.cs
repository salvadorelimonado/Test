namespace BusinessLayer.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsCommand
{
    public required long UserId { get; init; }

    public required long EnrollmentId { get; init; }

    public required int Seats { get; init; }
}
