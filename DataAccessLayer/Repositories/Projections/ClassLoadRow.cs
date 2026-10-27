namespace DataAccessLayer.Repositories.Projections;

public sealed class ClassLoadRow
{
    public DateTime Day { get; init; }

    public long RoomId { get; init; }

    public string RoomName { get; init; } = string.Empty;

    public int ClassesCount { get; init; }

    public int TotalCapacity { get; init; }

    public int BookedSeats { get; init; }
}
