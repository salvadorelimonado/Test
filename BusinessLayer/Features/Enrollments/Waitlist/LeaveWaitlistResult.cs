namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class LeaveWaitlistResult
{
    private LeaveWaitlistResult(LeaveWaitlistOutcome outcome, LeaveWaitlistResponse? response, string? reason)
    {
        Outcome = outcome;
        Response = response;
        Reason = reason;
    }

    public LeaveWaitlistOutcome Outcome { get; }

    public LeaveWaitlistResponse? Response { get; }

    public string? Reason { get; }

    public static LeaveWaitlistResult Succeeded(LeaveWaitlistResponse response)
    {
        return new LeaveWaitlistResult(LeaveWaitlistOutcome.Succeeded, response, null);
    }

    public static LeaveWaitlistResult NotFound()
    {
        return new LeaveWaitlistResult(LeaveWaitlistOutcome.NotFound, null, null);
    }

    public static LeaveWaitlistResult Conflict(string reason)
    {
        return new LeaveWaitlistResult(LeaveWaitlistOutcome.Conflict, null, reason);
    }
}
