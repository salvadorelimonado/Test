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
}
