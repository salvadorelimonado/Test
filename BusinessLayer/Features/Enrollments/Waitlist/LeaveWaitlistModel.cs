using BusinessLayer.Result;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistModel
{
    public required OperationResult<LeftWaitlist> Result { get; init; }

    public long? PromotedEnrollmentId { get; init; }

    public IReadOnlyList<long> PromotedUserIds { get; init; } = [];
}
