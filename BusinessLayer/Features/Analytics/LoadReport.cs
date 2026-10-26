namespace BusinessLayer.Features.Analytics;

public sealed class LoadReport
{
    public required List<LoadReportItem> Items { get; init; }
}

public sealed class LoadReportItem
{
    public required DateOnly Date { get; init; }

    public required LoadReportRoom Room { get; init; }

    public required int ClassesCount { get; init; }

    public required int TotalCapacity { get; init; }

    public required int BookedSeats { get; init; }
}

public sealed class LoadReportRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}
