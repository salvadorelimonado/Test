namespace WebApi.Tests.Infrastructure;

public abstract class ClubTest
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Club> Hosts = [];
    private static string? CurrentClass;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void OpenHost(TestContext testContext)
    {
        var className = testContext.FullyQualifiedTestClassName;
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new InvalidOperationException("Test class name is missing.");
        }

        var club = Club.Open();
        lock (Gate)
        {
            Hosts[className] = club;
            CurrentClass = className;
        }
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass, ClassCleanupBehavior.EndOfClass)]
    public static void ReleaseHost()
    {
        lock (Gate)
        {
            if (CurrentClass is null)
            {
                return;
            }

            if (Hosts.Remove(CurrentClass, out var club))
            {
                club.Dispose();
            }

            CurrentClass = null;
        }
    }

    public static void DisposeHosts()
    {
        lock (Gate)
        {
            foreach (var club in Hosts.Values)
            {
                club.Dispose();
            }

            Hosts.Clear();
            CurrentClass = null;
        }
    }

    protected Club Host()
    {
        var className = GetType().FullName;
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new InvalidOperationException("Test class name is missing.");
        }

        lock (Gate)
        {
            if (Hosts.TryGetValue(className, out var club))
            {
                return club;
            }
        }

        throw new InvalidOperationException("Test host is not initialized.");
    }
}
