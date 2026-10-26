using DataAccessLayer.Repositories;

namespace BusinessLayer.Features.Schedule;

public sealed class SchedulePageResult
{
    public required IReadOnlyList<ClassScheduleRow> Items { get; init; }

    public bool HasContinuation { get; init; }
}
