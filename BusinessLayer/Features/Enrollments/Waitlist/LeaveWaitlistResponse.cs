namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistResponse
{
    public required long Id { get; init; }

    public required string Status { get; init; }
}
