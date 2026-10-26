using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Schedule;

public sealed class GetScheduleService
{
    private readonly IUnitOfWork _unitOfWork;

    public GetScheduleService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ScheduleListing> GetAsync(GetScheduleQuery query, CancellationToken ct)
    {
        var rows = await _unitOfWork.Class.ListScheduleAsync(
            query.From,
            query.To,
            query.RoomId,
            query.UserId,
            query.Cursor?.StartsAt,
            query.Cursor?.ClassId,
            query.Limit + 1,
            ct);

        var hasMore = rows.Count > query.Limit;
        var visible = hasMore ? rows.Take(query.Limit).ToList() : rows;
        ScheduleCursor? next = null;
        if (hasMore)
        {
            var last = visible[visible.Count - 1];
            next = new ScheduleCursor(last.StartsAt, last.ClassId);
        }

        return new ScheduleListing
        {
            Items = visible.Select(row => new ScheduleListItem
            {
                ClassId = row.ClassId,
                Title = row.Title,
                StartsAt = row.StartsAt,
                DurationMinutes = row.DurationMinutes,
                Room = new ScheduleListRoom
                {
                    Id = row.RoomId,
                    Name = row.RoomName
                },
                Capacity = row.Capacity,
                FreeSeats = Math.Max(0, row.Capacity - row.TakenSeats),
                MySeats = row.MySeats
            }).ToList(),
            Next = next
        };
    }
}
