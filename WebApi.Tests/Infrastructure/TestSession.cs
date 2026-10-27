using Microsoft.Extensions.DependencyInjection;
using WebApi.Tests.Infrastructure.Time;

namespace WebApi.Tests.Infrastructure;

public sealed class TestSession
{
    private static ApiApplicationFactory? _factory;

    private TestSession()
    {
    }

    public DateTime Now => RequireFactory().Clock.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public static TestSession Open()
    {
        _factory ??= new ApiApplicationFactory();
        return new TestSession();
    }

    public static void Shutdown()
    {
        _factory?.Dispose();
        _factory = null;
    }

    public TestClock.FreezeScope Freeze(DateTime instant)
    {
        return RequireFactory().Clock.Freeze(instant);
    }

    public async Task InScope(Func<IServiceProvider, Task> work)
    {
        await using var scope = OpenScope();
        await work(scope.ServiceProvider);
    }

    internal AsyncServiceScope OpenScope()
    {
        return RequireFactory().Services.CreateAsyncScope();
    }

    internal static ApiApplicationFactory RequireFactory()
    {
        if (_factory is null)
        {
            throw new ArgumentNullException(nameof(_factory));
        }

        return _factory;
    }
}
