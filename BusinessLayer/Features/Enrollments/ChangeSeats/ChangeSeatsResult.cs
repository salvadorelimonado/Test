using BusinessLayer.Features.Enrollments;

namespace BusinessLayer.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsResult
{
    private ChangeSeatsResult(ChangeSeatsOutcome outcome, EnrollmentCard? card, string? reason)
    {
        Outcome = outcome;
        Card = card;
        Reason = reason;
    }

    public ChangeSeatsOutcome Outcome { get; }

    public EnrollmentCard? Card { get; }

    public string? Reason { get; }

    public static ChangeSeatsResult Succeeded(EnrollmentCard card)
    {
        return new ChangeSeatsResult(ChangeSeatsOutcome.Succeeded, card, null);
    }

    public static ChangeSeatsResult NotFound()
    {
        return new ChangeSeatsResult(ChangeSeatsOutcome.NotFound, null, null);
    }

    public static ChangeSeatsResult Conflict(string reason)
    {
        return new ChangeSeatsResult(ChangeSeatsOutcome.Conflict, null, reason);
    }
}
