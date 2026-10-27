using DataAccessLayer.Repositories.Projections;

namespace WebApi.Features.Analytics.GetLoad;

public static class LoadMapper
{
    public static LoadResponse ToResponse(IReadOnlyList<ClassLoadRow> rows)
    {
        return new LoadResponse
        {
            Items = rows.Select(row => new LoadResponseItem
            {
                Date = DateOnly.FromDateTime(row.Day),
                Room = new LoadResponseRoom
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
