using DataAccessLayer.Repositories.Projections;
using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Analytics;

public sealed class GetLoadHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public GetLoadHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<ClassLoadRow>> GetAsync(GetLoadQuery query, CancellationToken ct)
    {
        return await _unitOfWork.Class.ListLoadAsync(query.From, query.To, query.RoomId, ct);
    }
}
