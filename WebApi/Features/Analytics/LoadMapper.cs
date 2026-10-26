using BusinessLayer.Features.Analytics;

namespace WebApi.Features.Analytics;

public static class LoadMapper
{
    public static LoadResponse ToResponse(LoadListing listing)
    {
        return new LoadResponse
        {
            Items = listing.Items.Select(item => new LoadResponseItem
            {
                Date = item.Date,
                Room = new LoadResponseRoom
                {
                    Id = item.Room.Id,
                    Name = item.Room.Name
                },
                ClassesCount = item.ClassesCount,
                TotalCapacity = item.TotalCapacity,
                BookedSeats = item.BookedSeats
            }).ToList()
        };
    }
}
