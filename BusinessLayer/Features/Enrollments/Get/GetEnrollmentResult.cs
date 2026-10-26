namespace BusinessLayer.Features.Enrollments.Get;

public sealed class GetEnrollmentResult
{
    private GetEnrollmentResult(GetEnrollmentOutcome outcome, EnrollmentCard? card)
    {
        Outcome = outcome;
        Card = card;
    }

    public GetEnrollmentOutcome Outcome { get; }

    public EnrollmentCard? Card { get; }

    public static GetEnrollmentResult Found(EnrollmentCard card)
    {
        return new GetEnrollmentResult(GetEnrollmentOutcome.Found, card);
    }

    public static GetEnrollmentResult NotFound()
    {
        return new GetEnrollmentResult(GetEnrollmentOutcome.NotFound, null);
    }
}
