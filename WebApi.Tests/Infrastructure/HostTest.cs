using WebApi.Tests.Infrastructure.Http;
using WebApi.Tests.Infrastructure.Http.Clients;
using WebApi.Tests.Infrastructure.Stores;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Infrastructure;

public abstract class HostTest
{
    private static readonly DateTime FixedInstant = new(2032, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    protected static readonly TestSession Session = TestSession.Open();

    protected static readonly UsersStore Users = new(Session);

    protected static readonly PassesStore Passes = new(Session, Users);

    protected static readonly ClassesStore Classes = new(Session, Users);

    protected static readonly EnrollmentStore Enrollments = new(Session, Users);

    protected static readonly WaitlistStore Waitlist = new(Session, Users);

    private TestClock.FreezeScope? _clock;

    static HostTest()
    {
        AuthClient.Open(TestSession.RequireFactory().CreateClient());
    }

    [TestInitialize]
    public void FreezeTheClock()
    {
        _clock = Session.Freeze(FixedInstant);
    }

    [TestCleanup]
    public void ReleaseTheClock()
    {
        _clock?.Dispose();
        _clock = null;
    }

    public static void DisposeHosts()
    {
        AuthClient.Close();
    }
}
