namespace BusinessLayer.Result;

public enum ConflictReason
{
    CapacityExceeded,
    PassExhausted,
    PassExpired,
    ClassStarted,
    ClassCancelled,
    EnrollmentCancelled,
    IdempotencyMismatch,
    SeatsAvailable,
    AlreadyWaiting,
    AlreadyPromoted,
    ExceedsCapacity
}
