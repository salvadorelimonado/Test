namespace WebApi.Tests.Infrastructure;

public sealed class FrozenClock : IDisposable
{
    private readonly AdjustableTimeProvider _clock;
    private readonly DateTimeOffset? _previous;
    private int _disposed;

    public FrozenClock(AdjustableTimeProvider clock, DateTimeOffset instant)
    {
        _clock = clock;
        _previous = clock.Replace(instant);
    }

    public void MoveTo(DateTime instant)
    {
        if (instant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The instant must be UTC.", nameof(instant));
        }

        _clock.Replace(new DateTimeOffset(instant));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _clock.Restore(_previous);
    }
}
