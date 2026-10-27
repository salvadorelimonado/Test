using System.Net.Http.Json;
using System.Text;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.Tests.Infrastructure.DTO;

namespace WebApi.Tests.Infrastructure.Http.Clients;

public sealed class WaitlistClient
{
    private readonly string? _email;

    internal WaitlistClient(string? email)
    {
        _email = email;
    }

    public Task<HttpResponseMessage> JoinWaitlist(Lesson lesson, int seats, string? idempotencyKey = null)
    {
        return JoinWaitlist(lesson.ClassId, seats, idempotencyKey);
    }

    public Task<HttpResponseMessage> JoinWaitlist(long classId, int seats, string? idempotencyKey = null)
    {
        return Send(HttpMethod.Post, "/enrollments/waitlist", JsonContent.Create(new JoinWaitlistRequest
        {
            ClassId = classId,
            Seats = seats
        }), idempotencyKey ?? Guid.NewGuid().ToString("N"));
    }

    public Task<HttpResponseMessage> Post(string? json, string? idempotencyKey)
    {
        HttpContent? content = json is null
            ? null
            : new StringContent(json, Encoding.UTF8, "application/json");
        return Send(HttpMethod.Post, "/enrollments/waitlist", content, idempotencyKey);
    }

    public Task<HttpResponseMessage> JoinWaitlistWithoutKey(long classId, int seats)
    {
        return Send(HttpMethod.Post, "/enrollments/waitlist", JsonContent.Create(new JoinWaitlistRequest
        {
            ClassId = classId,
            Seats = seats
        }), null);
    }

    public async Task<JoinWaitlistResponse> Join(Lesson lesson, int seats, string? idempotencyKey = null)
    {
        var response = await JoinWaitlist(lesson, seats, idempotencyKey);
        response.EnsureSuccessStatusCode();
        return await response.ReadBody<JoinWaitlistResponse>();
    }

    public Task<HttpResponseMessage> LeaveWaitlist(long entryId)
    {
        return LeaveWaitlist(entryId.ToString());
    }

    public Task<HttpResponseMessage> LeaveWaitlist(string entryId)
    {
        return Send(HttpMethod.Post, $"/enrollments/waitlist/{entryId}/cancel", null, null);
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content, string? idempotencyKey)
    {
        return AuthClient.Send(_email, method, path, content, idempotencyKey);
    }
}
