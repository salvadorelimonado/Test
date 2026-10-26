namespace BusinessLayer.Features.Enrollments;

public sealed class EnrollmentCard
{
    public required long Id { get; init; }

    public required string Status { get; init; }

    public required int Seats { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required EnrollmentCardClass Class { get; init; }

    public required EnrollmentCardPass Pass { get; init; }
}

public sealed class EnrollmentCardClass
{
    public required long Id { get; init; }

    public required string Title { get; init; }

    public required DateTime StartsAt { get; init; }

    public required int DurationMinutes { get; init; }

    public required bool IsCancelled { get; init; }

    public required EnrollmentCardRoom Room { get; init; }
}

public sealed class EnrollmentCardRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}

public sealed class EnrollmentCardPass
{
    public required int RemainingVisits { get; init; }

    public required DateOnly ValidUntil { get; init; }
}
