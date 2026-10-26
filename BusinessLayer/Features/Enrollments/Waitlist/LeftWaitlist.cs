using DataAccessLayer.Entities;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeftWaitlist
{
    public required long Id { get; init; }

    public required WaitlistStatus Status { get; init; }
}
