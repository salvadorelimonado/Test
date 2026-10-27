namespace BusinessLayer.Features.Enrollments.LeaveWaitlist;

public sealed class LeaveWaitlistCommand
{
    public required long UserId { get; init; }

    public required long EntryId { get; init; }
}
