namespace BusinessLayer.Features.Analytics;

public sealed class GetLoadQuery
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public long? RoomId { get; init; }
}
