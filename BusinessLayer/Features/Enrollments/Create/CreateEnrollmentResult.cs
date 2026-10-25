namespace BusinessLayer.Features.Enrollments.Create;

public sealed class CreateEnrollmentResult
{
    private CreateEnrollmentResult(CreateEnrollmentOutcome outcome, CreateEnrollmentResponse? response, string? reason)
    {
        Outcome = outcome;
        Response = response;
        Reason = reason;
    }

    public CreateEnrollmentOutcome Outcome { get; }

    public CreateEnrollmentResponse? Response { get; }

    public string? Reason { get; }

    public static CreateEnrollmentResult Created(CreateEnrollmentResponse response)
    {
        return new CreateEnrollmentResult(CreateEnrollmentOutcome.Created, response, null);
    }

    public static CreateEnrollmentResult NotFound()
    {
        return new CreateEnrollmentResult(CreateEnrollmentOutcome.NotFound, null, null);
    }

    public static CreateEnrollmentResult Conflict(string reason)
    {
        return new CreateEnrollmentResult(CreateEnrollmentOutcome.Conflict, null, reason);
    }
}
