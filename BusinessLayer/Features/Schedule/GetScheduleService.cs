using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Schedule;

public sealed class GetScheduleService
{
    private readonly IUnitOfWork _unitOfWork;

    public GetScheduleService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<ClassScheduleRow>> GetAsync(GetScheduleQuery query, CancellationToken ct)
    {
        return await _unitOfWork.Class.ListScheduleAsync(
            query.From,
            query.To,
            query.RoomId,
            query.UserId,
            query.Cursor?.StartsAt,
            query.Cursor?.ClassId,
            query.Limit + 1,
            ct);
    }
}
