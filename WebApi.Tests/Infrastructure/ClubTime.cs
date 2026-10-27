namespace WebApi.Tests.Infrastructure;

public static class ClubTime
{
    private static Func<DateTime>? _utcNow;

    internal static void Use(Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
    }

    public static DateTime Instant()
    {
        var instant = _utcNow?.Invoke() ?? DateTime.UtcNow;
        if (instant.Kind == DateTimeKind.Utc)
        {
            return instant;
        }

        return DateTime.SpecifyKind(instant, DateTimeKind.Utc);
    }

    public static DateTime WholeSecond(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }

    public static DateOnly Today()
    {
        return DateOnly.FromDateTime(Instant());
    }
}
