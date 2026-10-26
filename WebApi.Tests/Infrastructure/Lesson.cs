namespace WebApi.Tests.Infrastructure;

public sealed class Lesson
{
    public const string DefaultTitle = "Class";

    public const int DefaultDurationMinutes = 60;

    public required long ClassId { get; init; }

    public required long UserId { get; init; }

    public required int UserNumber { get; init; }

    public required long RoomId { get; init; }

    public required string RoomName { get; init; }

    public required DateTime StartsAt { get; init; }

    public required DateOnly ValidUntil { get; init; }
}
