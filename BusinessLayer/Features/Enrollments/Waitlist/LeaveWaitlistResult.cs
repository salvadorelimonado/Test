namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistResult
{
    private LeaveWaitlistResult(
        LeaveWaitlistOutcome outcome,
        LeaveWaitlistResponse? response,
        string? reason,
        long? promotedEnrollmentId)
    {
        Outcome = outcome;
        Response = response;
        Reason = reason;
        PromotedEnrollmentId = promotedEnrollmentId;
    }

    public LeaveWaitlistOutcome Outcome { get; }

    public LeaveWaitlistResponse? Response { get; }

    public string? Reason { get; }

    public long? PromotedEnrollmentId { get; }

    public static LeaveWaitlistResult Succeeded(LeaveWaitlistResponse response)
    {
        return new LeaveWaitlistResult(LeaveWaitlistOutcome.Succeeded, response, null, null);
    }

    public static LeaveWaitlistResult NotFound()
    {
        return new LeaveWaitlistResult(LeaveWaitlistOutcome.NotFound, null, null, null);
    }

    public static LeaveWaitlistResult Conflict(string reason, long? promotedEnrollmentId = null)
    {
        return new LeaveWaitlistResult(LeaveWaitlistOutcome.Conflict, null, reason, promotedEnrollmentId);
    }
}
