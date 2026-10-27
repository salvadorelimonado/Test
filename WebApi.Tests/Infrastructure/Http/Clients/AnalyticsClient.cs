namespace WebApi.Tests.Infrastructure.Http.Clients;

public sealed class AnalyticsClient
{
    private readonly string? _email;

    internal AnalyticsClient(string? email)
    {
        _email = email;
    }

    public Task<HttpResponseMessage> Get()
    {
        return Get(null, null);
    }

    public Task<HttpResponseMessage> Get(string? from, string? to, string? roomId = null)
    {
        return AuthClient.Send(_email, HttpMethod.Get, Path(from, to, roomId), null, null);
    }

    public Task<HttpResponseMessage> Get(DateOnly from, DateOnly to, long? roomId = null)
    {
        return Get(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), roomId?.ToString());
    }

    private static string Path(string? from, string? to, string? roomId)
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

        if (roomId is not null)
        {
            parameters.Add($"roomId={Uri.EscapeDataString(roomId)}");
        }

        if (parameters.Count == 0)
        {
            return "/analytics/load";
        }

        return $"/analytics/load?{string.Join("&", parameters)}";
    }
}
