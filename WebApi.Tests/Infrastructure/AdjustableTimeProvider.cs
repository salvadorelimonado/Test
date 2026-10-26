namespace WebApi.Tests.Infrastructure;

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset? _instant;

    public override DateTimeOffset GetUtcNow()
    {
        return _instant ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset? Replace(DateTimeOffset instant)
    {
        var previous = _instant;
        _instant = instant;
        return previous;
    }

    public void Restore(DateTimeOffset? previous)
    {
        _instant = previous;
    }
}
