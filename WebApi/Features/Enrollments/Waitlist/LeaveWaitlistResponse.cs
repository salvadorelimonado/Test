using DataAccessLayer.Entities;

namespace WebApi.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistResponse
{
    public required long Id { get; init; }

    public required WaitlistStatus Status { get; init; }
}
