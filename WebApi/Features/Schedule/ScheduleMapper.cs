using BusinessLayer.Features.Schedule;

namespace WebApi.Features.Schedule;

public static class ScheduleMapper
{
    public static SchedulePage ToResponse(ScheduleListing listing)
    {
        string? continuationToken = null;
        if (listing.Next is ScheduleCursor next)
        {
            continuationToken = ScheduleContinuation.Write(next.StartsAt, next.ClassId);
        }

        return new SchedulePage
        {
            Items = listing.Items.Select(item => new ScheduleItem
            {
                ClassId = item.ClassId,
                Title = item.Title,
                StartsAt = item.StartsAt,
                DurationMinutes = item.DurationMinutes,
                Room = new ScheduleRoom
                {
                    Id = item.Room.Id,
                    Name = item.Room.Name
                },
                Capacity = item.Capacity,
                FreeSeats = item.FreeSeats,
                MySeats = item.MySeats
            }).ToList(),
            ContinuationToken = continuationToken
        };
    }
}
