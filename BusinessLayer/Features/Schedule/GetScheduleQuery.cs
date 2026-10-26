namespace BusinessLayer.Features.Schedule;

public sealed record ScheduleCursor(DateTime StartsAt, long ClassId);

public sealed class GetScheduleQuery
{
    public required long UserId { get; init; }

    public required DateTime From { get; init; }

    public required DateTime To { get; init; }

    public long? RoomId { get; init; }

    public required int Limit { get; init; }

    public ScheduleCursor? Cursor { get; init; }
}
