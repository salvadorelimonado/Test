namespace DataAccessLayer.Repositories;

public sealed class ClassLoadRow
{
    public DateTime Day { get; set; }

    public long RoomId { get; set; }

    public string RoomName { get; set; } = string.Empty;

    public int ClassesCount { get; set; }

    public int TotalCapacity { get; set; }

    public int BookedSeats { get; set; }
}
