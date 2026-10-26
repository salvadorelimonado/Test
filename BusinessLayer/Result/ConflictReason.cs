using System.Text.Json.Serialization;
using Common.Serialization;

namespace BusinessLayer.Result;

[JsonConverter(typeof(KebabCaseEnumConverter<ConflictReason>))]
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
    ExceedsCapacity,
    WaitlistNotEmpty
}
