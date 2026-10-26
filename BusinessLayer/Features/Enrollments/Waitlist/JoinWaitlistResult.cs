namespace BusinessLayer.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistResult
{
    private JoinWaitlistResult(JoinWaitlistOutcome outcome, JoinWaitlistResponse? response, string? reason)
    {
        Outcome = outcome;
        Response = response;
        Reason = reason;
    }

    public JoinWaitlistOutcome Outcome { get; }

    public JoinWaitlistResponse? Response { get; }

    public string? Reason { get; }

    public static JoinWaitlistResult Created(JoinWaitlistResponse response)
    {
        return new JoinWaitlistResult(JoinWaitlistOutcome.Created, response, null);
    }

    public static JoinWaitlistResult NotFound()
    {
        return new JoinWaitlistResult(JoinWaitlistOutcome.NotFound, null, null);
    }

    public static JoinWaitlistResult Conflict(string reason)
    {
        return new JoinWaitlistResult(JoinWaitlistOutcome.Conflict, null, reason);
    }
}
