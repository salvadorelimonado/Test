using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Features.Enrollments.ChangeSeats;
using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Features.Enrollments.Get;
using BusinessLayer.Features.Enrollments.JoinWaitlist;
using BusinessLayer.Features.Enrollments.LeaveWaitlist;
using WebApi.Controllers;
using WebApi.Features.Enrollments.ChangeSeats;
using WebApi.Features.Enrollments.Create;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.ActionResults;
using WebApi.RateLimiting;

namespace WebApi.Features.Enrollments;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimit.PolicyName)]
[Route("enrollments")]
public class EnrollmentsController : BaseApiController
{
    private readonly CreateEnrollmentHandler _createEnrollmentHandler;
    private readonly ChangeSeatsHandler _changeSeatsHandler;
    private readonly CancelEnrollmentHandler _cancelEnrollmentHandler;
    private readonly GetEnrollmentHandler _getEnrollmentHandler;
    private readonly JoinWaitlistHandler _joinWaitlistHandler;
    private readonly LeaveWaitlistHandler _leaveWaitlistHandler;

    public EnrollmentsController(
        CreateEnrollmentHandler createEnrollmentHandler,
        ChangeSeatsHandler changeSeatsHandler,
        CancelEnrollmentHandler cancelEnrollmentHandler,
        GetEnrollmentHandler getEnrollmentHandler,
        JoinWaitlistHandler joinWaitlistHandler,
        LeaveWaitlistHandler leaveWaitlistHandler)
    {
        _createEnrollmentHandler = createEnrollmentHandler;
        _changeSeatsHandler = changeSeatsHandler;
        _cancelEnrollmentHandler = cancelEnrollmentHandler;
        _getEnrollmentHandler = getEnrollmentHandler;
        _joinWaitlistHandler = joinWaitlistHandler;
        _leaveWaitlistHandler = leaveWaitlistHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateEnrollmentRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ProblemResultFactory.Create(StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ProblemResultFactory.Create(StatusCodes.Status400BadRequest);
        }

        var command = new CreateEnrollmentCommand
        {
            UserId = CurrentUserId,
            ClassId = request.ClassId,
            Seats = request.Seats,
            IdempotencyKey = idempotencyKey
        };
        var result = await _createEnrollmentHandler.CreateAsync(command, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status201Created);
    }

    [HttpPost("waitlist")]
    public async Task<IActionResult> JoinWaitlist(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JoinWaitlistRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ProblemResultFactory.Create(StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ProblemResultFactory.Create(StatusCodes.Status400BadRequest);
        }

        var command = new JoinWaitlistCommand
        {
            UserId = CurrentUserId,
            ClassId = request.ClassId,
            Seats = request.Seats,
            IdempotencyKey = idempotencyKey
        };
        var result = await _joinWaitlistHandler.JoinAsync(command, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status201Created);
    }

    [HttpPost("waitlist/{id}/cancel")]
    public async Task<IActionResult> LeaveWaitlist(long id, CancellationToken ct)
    {
        var command = new LeaveWaitlistCommand
        {
            UserId = CurrentUserId,
            EntryId = id
        };
        var result = await _leaveWaitlistHandler.LeaveAsync(command, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status200OK);
    }

    [HttpPost("{id}/seats")]
    public async Task<IActionResult> ChangeSeats(
        long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ChangeSeatsRequest? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ProblemResultFactory.Create(StatusCodes.Status400BadRequest);
        }
        var command = new ChangeSeatsCommand
        {
            UserId = CurrentUserId,
            EnrollmentId = id,
            Seats = request.Seats
        };
        var result = await _changeSeatsHandler.ChangeAsync(command, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status200OK);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var command = new CancelEnrollmentCommand
        {
            UserId = CurrentUserId,
            EnrollmentId = id
        };
        var result = await _cancelEnrollmentHandler.CancelAsync(command, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status200OK);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var query = new GetEnrollmentQuery
        {
            UserId = CurrentUserId,
            EnrollmentId = id
        };
        var result = await _getEnrollmentHandler.GetAsync(query, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status200OK);
    }
}
