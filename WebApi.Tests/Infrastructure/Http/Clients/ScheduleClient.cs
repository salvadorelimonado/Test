using System.Globalization;

namespace WebApi.Tests.Infrastructure.Http.Clients;

public sealed class ScheduleClient
{
    private readonly string? _email;

    internal ScheduleClient(string? email)
    {
        _email = email;
    }

    public Task<HttpResponseMessage> Get()
    {
        return Get(null, null);
    }

    public Task<HttpResponseMessage> Get(
        string? from,
        string? to,
        int? limit = null,
        long? roomId = null,
        string? continuation = null)
    {
        return AuthClient.Send(_email, HttpMethod.Get, Path(from, to, limit, roomId, continuation), null, null);
    }

    public Task<HttpResponseMessage> Get(
        DateTime from,
        DateTime to,
        int? limit = null,
        long? roomId = null,
        string? continuation = null)
    {
        return Get(
            from.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            to.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            limit,
            roomId,
            continuation);
    }

    private static string Path(string? from, string? to, int? limit, long? roomId, string? continuation)
    {
        var parameters = new List<string>();
        if (from is not null)
        {
            parameters.Add($"from={Uri.EscapeDataString(from)}");
        }

        if (to is not null)
        {
            parameters.Add($"to={Uri.EscapeDataString(to)}");
        }

        if (limit is int pageSize)
        {
            parameters.Add($"limit={pageSize}");
        }

        if (roomId is long selectedRoomId)
        {
            parameters.Add($"roomId={selectedRoomId}");
        }

        if (continuation is not null)
        {
            parameters.Add($"continuationToken={Uri.EscapeDataString(continuation)}");
        }

        if (parameters.Count == 0)
        {
            return "/schedule";
        }

        return $"/schedule?{string.Join("&", parameters)}";
    }
}
