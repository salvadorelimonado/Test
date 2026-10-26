namespace WebApi.Tests.Infrastructure;

public abstract class ClubTest
{
    private static readonly object Gate = new();
    private static Club? Shared;

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
