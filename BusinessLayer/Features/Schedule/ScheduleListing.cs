namespace BusinessLayer.Features.Schedule;

public sealed class ScheduleListing
{
    public required List<ScheduleListItem> Items { get; init; }

    public ScheduleCursor? Next { get; init; }
}

public sealed class ScheduleListItem
{
    public required long ClassId { get; init; }

    public required string Title { get; init; }

    public required DateTime StartsAt { get; init; }

    public required int DurationMinutes { get; init; }

    public required ScheduleListRoom Room { get; init; }

    public required int Capacity { get; init; }

    public required int FreeSeats { get; init; }

    public required int MySeats { get; init; }
}

public sealed class ScheduleListRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}
