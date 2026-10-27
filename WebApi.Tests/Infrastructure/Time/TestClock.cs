namespace WebApi.Tests.Infrastructure.Time;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset? _instant;

    public override DateTimeOffset GetUtcNow()
    {
        return _instant ?? DateTimeOffset.UtcNow;
    }

    public FreezeScope Freeze(DateTime instant)
    {
        if (instant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The instant must be UTC.", nameof(instant));
        }

        return new FreezeScope(this, instant);
    }

    private DateTimeOffset? Replace(DateTimeOffset instant)
    {
        var previous = _instant;
        _instant = instant;
        return previous;
    }

    private void Restore(DateTimeOffset? previous)
    {
        _instant = previous;
    }

    public sealed class FreezeScope : IDisposable
    {
        private readonly TestClock _clock;
        private readonly DateTimeOffset? _previous;
        private int _disposed;

        internal FreezeScope(TestClock clock, DateTime instant)
        {
            _clock = clock;
            _previous = clock.Replace(new DateTimeOffset(instant));
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
}
