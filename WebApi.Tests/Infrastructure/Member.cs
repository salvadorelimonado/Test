using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using WebApi.Features.Enrollments.ChangeSeats;
using WebApi.Features.Enrollments.Create;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.RateLimiting;

namespace WebApi.Tests.Infrastructure;

public sealed class Member
{
    private readonly Club _club;
    private readonly string? _email;

    public Member(Club club, int number, string? email)
    {
        _club = club;
        Number = number;
        _email = email;
    }

    public int Number { get; }

    public Task<HttpResponseMessage> Enroll(Lesson lesson, int seats, string? idempotencyKey = null)
    {
        return Enroll(lesson.ClassId, seats, idempotencyKey);
    }

    public Task<HttpResponseMessage> Enroll(long classId, int seats, string? idempotencyKey = null)
    {
        return Send(HttpMethod.Post, "/enrollments", JsonContent.Create(new CreateEnrollmentRequest
        {
            ClassId = classId,
            Seats = seats
        }), idempotencyKey ?? Guid.NewGuid().ToString("N"));
    }

    public Task<HttpResponseMessage> Post(string path, string? json, string? idempotencyKey)
    {
        HttpContent? content = json is null
            ? null
            : new StringContent(json, Encoding.UTF8, "application/json");
        return Send(HttpMethod.Post, path, content, idempotencyKey);
    }

    public Task<HttpResponseMessage> EnrollWithoutKey(long classId, int seats)
    {
        return Send(HttpMethod.Post, "/enrollments", JsonContent.Create(new CreateEnrollmentRequest
        {
            ClassId = classId,
            Seats = seats
        }), null);
    }

    public async Task<CreateEnrollmentResponse> Book(Lesson lesson, int seats)
    {
        var response = await Enroll(lesson, seats);
        response.EnsureSuccessStatusCode();
        return await response.ReadBody<CreateEnrollmentResponse>();
    }

    public async Task<CreateEnrollmentResponse> Book(long classId, int seats)
    {
        var response = await Enroll(classId, seats);
        response.EnsureSuccessStatusCode();
        return await response.ReadBody<CreateEnrollmentResponse>();
    }

    public Task<HttpResponseMessage> ChangeSeats(long enrollmentId, int seats)
    {
        return ChangeSeats(enrollmentId.ToString(), seats);
    }

    public Task<HttpResponseMessage> ChangeSeats(string enrollmentId, int seats)
    {
        return Send(
            HttpMethod.Post,
            $"/enrollments/{enrollmentId}/seats",
            JsonContent.Create(new ChangeSeatsRequest { Seats = seats }),
            null);
    }

    public Task<HttpResponseMessage> CancelEnrollment(long enrollmentId)
    {
        return CancelEnrollment(enrollmentId.ToString());
    }

    public Task<HttpResponseMessage> CancelEnrollment(string enrollmentId)
    {
        return Send(HttpMethod.Post, $"/enrollments/{enrollmentId}/cancel", null, null);
    }

    public Task<HttpResponseMessage> GetEnrollment(long enrollmentId)
    {
        return GetEnrollment(enrollmentId.ToString());
    }

    public Task<HttpResponseMessage> GetEnrollment(string enrollmentId)
    {
        return Send(HttpMethod.Get, $"/enrollments/{enrollmentId}", null, null);
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

    public Task<HttpResponseMessage> Schedule()
    {
        return Schedule(null, null);
    }

    public Task<HttpResponseMessage> Schedule(
        string? from,
        string? to,
        int? limit = null,
        long? roomId = null,
        string? continuation = null)
    {
        return Send(HttpMethod.Get, SchedulePath(from, to, limit, roomId, continuation), null, null);
    }

    public Task<HttpResponseMessage> Schedule(
        DateTime from,
        DateTime to,
        int? limit = null,
        long? roomId = null,
        string? continuation = null)
    {
        return Schedule(
            from.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            to.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            limit,
            roomId,
            continuation);
    }

    public Task<HttpResponseMessage> Load()
    {
        return Load(null, null);
    }

    public Task<HttpResponseMessage> Load(string? from, string? to, string? roomId = null)
    {
        return Send(HttpMethod.Get, LoadPath(from, to, roomId), null, null);
    }

    public Task<HttpResponseMessage> Load(DateOnly from, DateOnly to, long? roomId = null)
    {
        return Load(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), roomId?.ToString());
    }

    public async Task<HttpStatusCode> Hammer(Func<int, Task<HttpResponseMessage>> send)
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

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(method, path);
        if (_email is not null)
        {
            var token = await _club.Token(_email);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        request.Content = content;
        return await _club.Http.SendAsync(request);
    }

    private static string SchedulePath(string? from, string? to, int? limit, long? roomId, string? continuation)
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

    private static string LoadPath(string? from, string? to, string? roomId)
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
