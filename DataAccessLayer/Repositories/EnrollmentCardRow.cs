using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public sealed class EnrollmentCardRow
{
    public long Id { get; init; }

    public EnrollmentStatus Status { get; init; }

    public int Seats { get; init; }

    public DateTime CreatedAt { get; init; }

    public long ClassId { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTime StartsAt { get; init; }

    public int DurationMinutes { get; init; }

    public bool IsCancelled { get; init; }

    public long RoomId { get; init; }

    public string RoomName { get; init; } = string.Empty;

    public EnrollmentCardPassRow? Pass { get; init; }
}

public sealed class EnrollmentCardPassRow
{
    public int RemainingVisits { get; init; }

    public DateOnly ValidUntil { get; init; }
}
