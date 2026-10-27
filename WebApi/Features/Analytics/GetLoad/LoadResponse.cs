namespace WebApi.Features.Analytics.GetLoad;

public sealed class LoadResponse
{
    public required List<LoadResponseItem> Items { get; init; }
}

public sealed class LoadResponseItem
{
    public required DateOnly Date { get; init; }

    public required LoadResponseRoom Room { get; init; }

    public required int ClassesCount { get; init; }

    public required int TotalCapacity { get; init; }

    public required int BookedSeats { get; init; }
}

public sealed class LoadResponseRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}
