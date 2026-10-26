using BusinessLayer.Result;

namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistModel
{
    public required OperationResult<LeaveWaitlistResponse> Result { get; init; }

    public long? PromotedEnrollmentId { get; init; }
}
