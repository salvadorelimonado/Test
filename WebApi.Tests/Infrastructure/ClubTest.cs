namespace WebApi.Tests.Infrastructure;

public abstract class ClubTest
{
    private static readonly object Gate = new();
    private static readonly DateTime FixedInstant = new(2032, 6, 1, 9, 0, 0, DateTimeKind.Utc);
    private static Club? Shared;
    private FrozenClock? _clock;

    [TestInitialize]
    public void FreezeTheClock()
    {
        _clock = Host().Freeze(FixedInstant);
    }

    [TestCleanup]
    public void ReleaseTheClock()
    {
        _clock?.Dispose();
        _clock = null;
    }

    protected Club Host()
    {
        lock (Gate)
        {
            Shared ??= Club.Open();
            return Shared;
        }
    }

    public static void DisposeHosts()
    {
        lock (Gate)
        {
            if (Shared is null)
            {
                return;
            }

            Shared.Dispose();
            Shared = null;
        }
    }
}
