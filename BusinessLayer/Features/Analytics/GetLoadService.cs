using DataAccessLayer.UnitOfWork;

namespace BusinessLayer.Features.Analytics;

public sealed class GetLoadService
{
    private readonly IUnitOfWork _unitOfWork;

    public GetLoadService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<LoadListing> GetAsync(GetLoadQuery query, CancellationToken ct)
    {
        var rows = await _unitOfWork.Class.ListLoadAsync(query.From, query.To, query.RoomId, ct);
        return new LoadListing
        {
            Items = rows.Select(row => new LoadListingItem
            {
                Date = DateOnly.FromDateTime(row.Day),
                Room = new LoadListingRoom
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
