using BusinessLayer.Features.Schedule;

namespace WebApi.Features.Schedule;

public static class ScheduleMapper
{
    public static SchedulePage ToResponse(SchedulePageResult page)
    {
        string? continuationToken = null;
        if (page.HasContinuation)
        {
            var last = page.Items[page.Items.Count - 1];
            continuationToken = ScheduleContinuation.Write(last.StartsAt, last.ClassId);
        }

        return new SchedulePage
        {
            Items = page.Items.Select(row => new ScheduleItem
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
                FreeSeats = row.FreeSeats,
                MySeats = row.MySeats
            }).ToList(),
            ContinuationToken = continuationToken
        };
    }
}
