using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Analytics;

public sealed class GetLoadService
{
    private readonly IUnitOfWork _unitOfWork;

    public GetLoadService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<LoadReport> GetAsync(GetLoadQuery query, CancellationToken ct)
    {
        var rows = await _unitOfWork.Class.ListLoadAsync(query.From, query.To, query.RoomId, ct);
        return new LoadReport
        {
            Items = rows.Select(row => new LoadReportItem
            {
                Date = DateOnly.FromDateTime(row.Day),
                Room = new LoadReportRoom
                {
                    Id = row.RoomId,
                    Name = row.RoomName
                },
                ClassesCount = row.ClassesCount,
                TotalCapacity = row.TotalCapacity,
                BookedSeats = row.BookedSeats
            }).ToList()
        };
    }
}
