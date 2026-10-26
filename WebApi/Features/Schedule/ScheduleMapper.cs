using DataAccessLayer.Repositories;

namespace WebApi.Features.Schedule;

public static class ScheduleMapper
{
    public static SchedulePage ToResponse(IReadOnlyList<ClassScheduleRow> rows, int limit)
    {
        var hasMore = rows.Count > limit;
        var visible = hasMore ? rows.Take(limit).ToList() : rows;
        string? continuationToken = null;
        if (hasMore)
        {
            var last = visible[visible.Count - 1];
            continuationToken = ScheduleContinuation.Write(last.StartsAt, last.ClassId);
        }

        return new SchedulePage
        {
            Items = visible.Select(row => new ScheduleItem
            {
                ClassId = row.ClassId,
                Title = row.Title,
                StartsAt = row.StartsAt,
                DurationMinutes = row.DurationMinutes,
                Room = new ScheduleRoom
                {
                    Id = row.RoomId,
                    Name = row.RoomName
                },
                Capacity = row.Capacity,
                FreeSeats = Math.Max(0, row.Capacity - row.TakenSeats),
                MySeats = row.MySeats
            }).ToList(),
            ContinuationToken = continuationToken
        };
    }
}
