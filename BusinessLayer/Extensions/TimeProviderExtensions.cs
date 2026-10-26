namespace BusinessLayer.Extensions;

public static class TimeProviderExtensions
{
    public static DateTime TruncateToMicroseconds(this TimeProvider clock)
    {
        var value = clock.GetUtcNow().UtcDateTime;
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
