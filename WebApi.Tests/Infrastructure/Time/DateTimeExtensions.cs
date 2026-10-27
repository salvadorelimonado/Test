namespace WebApi.Tests.Infrastructure.Time;

public static class DateTimeExtensions
{
    public static DateTime WholeSecond(this DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }
}
