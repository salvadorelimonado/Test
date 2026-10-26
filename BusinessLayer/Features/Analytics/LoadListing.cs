namespace BusinessLayer.Features.Analytics;

public sealed class LoadListing
{
    public required List<LoadListingItem> Items { get; init; }
}

public sealed class LoadListingItem
{
    public required DateOnly Date { get; init; }

    public required LoadListingRoom Room { get; init; }

    public required int ClassesCount { get; init; }

    public required int TotalCapacity { get; init; }

    public required int BookedSeats { get; init; }
}

public sealed class LoadListingRoom
{
    public required long Id { get; init; }

    public required string Name { get; init; }
}
