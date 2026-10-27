using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace WebApi.Features.Schedule;

public static class ScheduleContinuation
{
    public static string Write(DateTime startsAt, long classId)
    {
        // решение: токен — позиция страницы без подписи и без привязки к фильтрам.
        var payload = string.Create(CultureInfo.InvariantCulture, $"{startsAt.Ticks}:{classId}");
        return Encode(payload);
    }

    public static bool TryRead(string? token, out DateTime startsAt, out long classId)
    {
        startsAt = default;
        classId = 0;
        if (string.IsNullOrWhiteSpace(token) || !TryDecode(token, out var payload))
        {
            return false;
        }

        var separator = payload.IndexOf(':');
        if (separator <= 0 || separator != payload.LastIndexOf(':'))
        {
            return false;
        }

        var ticksText = payload[..separator];
        var classIdText = payload[(separator + 1)..];
        if (!long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !long.TryParse(classIdText, NumberStyles.None, CultureInfo.InvariantCulture, out classId)
            || classId <= 0)
        {
            return false;
        }

        try
        {
            startsAt = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return true;
    }

    private static string Encode(string payload)
    {
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryDecode(string token, out string payload)
    {
        payload = string.Empty;
        try
        {
            payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
