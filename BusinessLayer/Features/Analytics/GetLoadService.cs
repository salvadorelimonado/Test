using DataAccessLayer.Repositories;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Analytics;

public sealed class GetLoadService
{
    private readonly IUnitOfWork _unitOfWork;

    public GetLoadService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<ClassLoadRow>> GetAsync(GetLoadQuery query, CancellationToken ct)
    {
        return await _unitOfWork.Class.ListLoadAsync(query.From, query.To, query.RoomId, ct);
    }
}
