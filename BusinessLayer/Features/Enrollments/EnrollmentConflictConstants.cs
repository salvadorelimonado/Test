namespace BusinessLayer.Features.Enrollments;

public static class EnrollmentConflictConstants
{
    public const string CapacityExceeded = "capacity-exceeded";

    public const string PassExhausted = "pass-exhausted";

    public const string PassExpired = "pass-expired";

    public const string ClassStarted = "class-started";

    public const string ClassCancelled = "class-cancelled";

    public const string EnrollmentCancelled = "enrollment-cancelled";

    public const string IdempotencyMismatch = "idempotency-mismatch";

    public const string SeatsAvailable = "seats-available";

    public const string AlreadyWaiting = "already-waiting";

    public const string AlreadyPromoted = "already-promoted";

    public const string ExceedsCapacity = "exceeds-capacity";

    public const string WaitlistNotEmpty = "waitlist-not-empty";
}
