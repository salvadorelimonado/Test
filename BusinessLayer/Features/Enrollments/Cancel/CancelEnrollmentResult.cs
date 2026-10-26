namespace BusinessLayer.Features.Enrollments.Cancel;

public sealed class CancelEnrollmentResult
{
    private CancelEnrollmentResult(CancelEnrollmentOutcome outcome, CancelEnrollmentResponse? response, string? reason)
    {
        Outcome = outcome;
        Response = response;
        Reason = reason;
    }

    public CancelEnrollmentOutcome Outcome { get; }

    public CancelEnrollmentResponse? Response { get; }

    public string? Reason { get; }

    public static CancelEnrollmentResult Succeeded(CancelEnrollmentResponse response)
    {
        return new CancelEnrollmentResult(CancelEnrollmentOutcome.Succeeded, response, null);
    }

    public static CancelEnrollmentResult NotFound()
    {
        return new CancelEnrollmentResult(CancelEnrollmentOutcome.NotFound, null, null);
    }

    public static CancelEnrollmentResult Conflict(string reason)
    {
        return new CancelEnrollmentResult(CancelEnrollmentOutcome.Conflict, null, reason);
    }
}
