namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistCommand
{
    public required long UserId { get; init; }

    public required long EntryId { get; init; }
}
