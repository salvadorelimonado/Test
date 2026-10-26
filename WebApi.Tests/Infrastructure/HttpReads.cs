using System.Net.Http.Json;
using System.Text.Json;
using BusinessLayer.Result;
using Shouldly;

namespace WebApi.Tests.Infrastructure;

public static class HttpReads
{
    public static async Task<T> ReadBody<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>();
        if (body is null)
        {
            throw new InvalidOperationException("Response body is empty.");
        }

        return body;
    }

    public static async Task<ConflictReason> ReadReason(this HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var reason = document.RootElement.GetProperty("reason");
        if (reason.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Conflict response has no reason.");
        }

        return reason.Deserialize<ConflictReason>();
    }

    public static async Task<string> ReadReasonText(this HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("reason").GetString()
            ?? throw new InvalidOperationException("Conflict response has no reason.");
    }

    public static async Task<JsonElement> ReadJson(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static void ShouldHaveProperties(this JsonElement element, params string[] names)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
        var expected = names.OrderBy(name => name).ToArray();
        actual.ShouldBe(expected);
    }
}
