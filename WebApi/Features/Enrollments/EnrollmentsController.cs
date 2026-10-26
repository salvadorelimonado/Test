using BusinessLayer.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Features.Enrollments.ChangeSeats;
using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Features.Enrollments.Get;
using BusinessLayer.Features.Enrollments.Waitlist;
using WebApi.Controllers;
using WebApi.Features.Enrollments.ChangeSeats;
using WebApi.Features.Enrollments.Create;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.Helpers;
using WebApi.RateLimiting;

namespace WebApi.Features.Enrollments;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimit.PolicyName)]
[Route("enrollments")]
public class EnrollmentsController : BaseApiController
{
    private readonly CreateEnrollmentService _createEnrollmentService;
    private readonly ChangeSeatsService _changeSeatsService;
    private readonly CancelEnrollmentService _cancelEnrollmentService;
    private readonly GetEnrollmentService _getEnrollmentService;
    private readonly JoinWaitlistService _joinWaitlistService;
    private readonly LeaveWaitlistService _leaveWaitlistService;

    public EnrollmentsController(
        CreateEnrollmentService createEnrollmentService,
        ChangeSeatsService changeSeatsService,
        CancelEnrollmentService cancelEnrollmentService,
        GetEnrollmentService getEnrollmentService,
        JoinWaitlistService joinWaitlistService,
        LeaveWaitlistService leaveWaitlistService)
    {
        _createEnrollmentService = createEnrollmentService;
        _changeSeatsService = changeSeatsService;
        _cancelEnrollmentService = cancelEnrollmentService;
        _getEnrollmentService = getEnrollmentService;
        _joinWaitlistService = joinWaitlistService;
        _leaveWaitlistService = leaveWaitlistService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateEnrollmentRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        var command = new CreateEnrollmentCommand
        {
            UserId = CurrentUserId,
            ClassId = request.ClassId,
            Seats = request.Seats,
            IdempotencyKey = idempotencyKey
        };
        var result = await _createEnrollmentService.CreateAsync(command, ct);
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
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        var command = new JoinWaitlistCommand
        {
            UserId = CurrentUserId,
            ClassId = request.ClassId,
            Seats = request.Seats,
            IdempotencyKey = idempotencyKey
        };
        var result = await _joinWaitlistService.JoinAsync(command, ct);
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
        var model = await _leaveWaitlistService.LeaveAsync(command, ct);
        return ToLeaveActionResult(model.Result.Select(EnrollmentMapper.ToResponse), model.PromotedEnrollmentId);
    }

    [HttpPost("{id}/seats")]
    public async Task<IActionResult> ChangeSeats(
        long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ChangeSeatsRequest? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }
        var command = new ChangeSeatsCommand
        {
            UserId = CurrentUserId,
            EnrollmentId = id,
            Seats = request.Seats
        };
        var result = await _changeSeatsService.ChangeAsync(command, ct);
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
        var result = await _cancelEnrollmentService.CancelAsync(command, ct);
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
        var result = await _getEnrollmentService.GetAsync(query, ct);
        return result.Select(EnrollmentMapper.ToResponse).ToActionResult(StatusCodes.Status200OK);
    }

    private static IActionResult ToLeaveActionResult(
        OperationResult<LeaveWaitlistResponse> result,
        long? promotedEnrollmentId)
    {
        var actionResult = result.ToActionResult(StatusCodes.Status200OK);
        if (promotedEnrollmentId is not long enrollmentId)
        {
            return actionResult;
        }

        if (actionResult is not ObjectResult problem)
        {
            throw new ArgumentNullException(nameof(actionResult));
        }

        if (problem.Value is not ProblemDetails details)
        {
            throw new ArgumentNullException(nameof(problem.Value));
        }

        details.Extensions["promotedEnrollmentId"] = enrollmentId;
        return actionResult;
    }
}
