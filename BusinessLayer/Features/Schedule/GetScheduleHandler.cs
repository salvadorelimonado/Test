using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Schedule;

public sealed class GetScheduleHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public GetScheduleHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SchedulePageResult> GetAsync(GetScheduleQuery query, CancellationToken ct)
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
        var hasContinuation = rows.Count > query.Limit;
        if (hasContinuation)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        foreach (var row in rows)
        {
            var freeSeats = row.Capacity - row.TakenSeats;
            row.FreeSeats = freeSeats > 0 ? freeSeats : 0;
        }

        return new SchedulePageResult
        {
            Items = rows,
            HasContinuation = hasContinuation
        };
    }
}
