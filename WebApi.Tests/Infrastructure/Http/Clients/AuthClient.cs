using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataAccessLayer.Seeding;
using WebApi.Auth;
using WebApi.RateLimiting;

namespace WebApi.Tests.Infrastructure.Http.Clients;

public static class AuthClient
{
    private static readonly Dictionary<string, string> Tokens = [];
    private static HttpClient? _http;

    internal static void Open(HttpClient http)
    {
        if (http is null)
        {
            throw new ArgumentNullException(nameof(http));
        }

        _http = http;
    }

    internal static void Close()
    {
        if (_http is null)
        {
            return;
        }

        _http.Dispose();
        _http = null;
        Tokens.Clear();
    }

    public static async Task<HttpStatusCode> Hammer(Func<int, Task<HttpResponseMessage>> send)
    {
        HttpStatusCode last = default;
        var blockedRequestNumber = ApiRateLimit.PermitLimit + 1;
        for (var requestNumber = 1; requestNumber <= blockedRequestNumber; requestNumber++)
        {
            var response = await send(requestNumber);
            last = response.StatusCode;
        }

        return last;
    }

    internal static async Task<HttpResponseMessage> Send(
        string? email,
        HttpMethod method,
        string path,
        HttpContent? content,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(method, path);
        if (email is not null)
        {
            var token = await Token(email);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        request.Content = content;
        return await Client().SendAsync(request);
    }

    private static async Task<string> Token(string email)
    {
        if (Tokens.TryGetValue(email, out var cached))
        {
            return cached;
        }

        var response = await Client().PostAsJsonAsync(
            "/auth/login",
            new LoginRequest { Email = email, Password = DatabaseSeeder.DefaultPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.ReadBody<LoginResponse>();
        if (string.IsNullOrWhiteSpace(body.AccessToken))
        {
            throw new InvalidOperationException("Login did not return an access token.");
        }

        Tokens[email] = body.AccessToken;
        return body.AccessToken;
    }

    private static HttpClient Client()
    {
        if (_http is null)
        {
            throw new ArgumentNullException(nameof(_http));
        }

        return _http;
    }
}
