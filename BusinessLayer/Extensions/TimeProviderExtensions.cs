namespace BusinessLayer.Extensions;

public static class TimeProviderExtensions
{
    public static DateTime TruncateToMicroseconds(this TimeProvider clock)
    {
        // решение: время усекаем до микросекунды, иначе одна и та же бронь отдаёт разное
        // значение CreatedAt при повторном чтении из Postgres.
        var value = clock.GetUtcNow().UtcDateTime;
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
