using System.Globalization;
using System.Text;

namespace WebApi.Features.Schedule;

public static class ScheduleContinuation
{
    public static string Write(DateTime startsAt, long classId)
    {
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
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecode(string token, out string payload)
    {
        payload = string.Empty;
        var padded = token.Replace('-', '+').Replace('_', '/');
        var remainder = padded.Length % 4;
        if (remainder == 1)
        {
            return false;
        }

        if (remainder > 0)
        {
            padded = padded.PadRight(padded.Length + (4 - remainder), '=');
        }

        try
        {
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
