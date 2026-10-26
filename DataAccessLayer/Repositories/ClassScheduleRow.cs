namespace DataAccessLayer.Repositories;

public sealed class ClassScheduleRow
{
    public long ClassId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }

    public int DurationMinutes { get; set; }

    public int Capacity { get; set; }

    public long RoomId { get; set; }

    public string RoomName { get; set; } = string.Empty;

    public int TakenSeats { get; set; }

    public int FreeSeats { get; set; }

    public int MySeats { get; set; }
}
