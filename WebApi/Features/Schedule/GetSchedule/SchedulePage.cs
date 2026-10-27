namespace WebApi.Features.Schedule.GetSchedule;

public sealed class SchedulePage
{
    public required List<ScheduleItem> Items { get; init; }

    public string? ContinuationToken { get; init; }
}

public sealed class ScheduleItem
{
    public required long ClassId { get; init; }

    public required string Title { get; init; }

    public required DateTime StartsAt { get; init; }

    public required int DurationMinutes { get; init; }

    public required ScheduleRoom Room { get; init; }

    public required int Capacity { get; init; }

    public required int FreeSeats { get; init; }

    public required int MySeats { get; init; }
}

public sealed class ScheduleRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}
