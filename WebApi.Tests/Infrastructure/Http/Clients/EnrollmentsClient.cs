using System.Net.Http.Json;
using System.Text;
using WebApi.Features.Enrollments.ChangeSeats;
using WebApi.Features.Enrollments.Create;
using WebApi.Tests.Infrastructure.DTO;

namespace WebApi.Tests.Infrastructure.Http.Clients;

public sealed class EnrollmentsClient
{
    private readonly string? _email;

    internal EnrollmentsClient(string? email)
    {
        _email = email;
    }

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

    public Task<HttpResponseMessage> Post(string? json, string? idempotencyKey)
    {
        HttpContent? content = json is null
            ? null
            : new StringContent(json, Encoding.UTF8, "application/json");
        return Send(HttpMethod.Post, "/enrollments", content, idempotencyKey);
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

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content, string? idempotencyKey)
    {
        return AuthClient.Send(_email, method, path, content, idempotencyKey);
    }
}
