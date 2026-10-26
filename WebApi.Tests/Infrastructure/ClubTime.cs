namespace WebApi.Tests.Infrastructure;

public static class ClubTime
{
    public static DateTime WholeSecond(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }

    public static DateOnly Today()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
