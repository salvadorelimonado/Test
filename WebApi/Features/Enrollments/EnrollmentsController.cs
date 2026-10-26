using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Features.Enrollments.ChangeSeats;
using BusinessLayer.Features.Enrollments.Create;
using BusinessLayer.Features.Enrollments.Get;
using BusinessLayer.Features.Enrollments.Waitlist;
using WebApi.Controllers;
using WebApi.Features.Enrollments.Cancel;
using WebApi.Features.Enrollments.ChangeSeats;
using WebApi.Features.Enrollments.Create;
using WebApi.Features.Enrollments.Get;
using WebApi.Features.Enrollments.Waitlist;
using WebApi.Helpers;
using WebApi.Idempotency;
using WebApi.RateLimiting;

namespace WebApi.Features.Enrollments;

[ApiController]
[Authorize]
[EnableRateLimiting(EnrollmentRateLimit.PolicyName)]
[Route("enrollments")]
public class EnrollmentsController : BaseApiController
{
    private readonly CreateEnrollmentService _createEnrollmentService;
    private readonly ChangeSeatsService _changeSeatsService;
    private readonly CancelEnrollmentService _cancelEnrollmentService;
    private readonly GetEnrollmentService _getEnrollmentService;
    private readonly JoinWaitlistService _joinWaitlistService;
    private readonly LeaveWaitlistService _leaveWaitlistService;
    private readonly IdempotencyKeyReader _idempotencyKeyReader;

    public EnrollmentsController(
        CreateEnrollmentService createEnrollmentService,
        ChangeSeatsService changeSeatsService,
        CancelEnrollmentService cancelEnrollmentService,
        GetEnrollmentService getEnrollmentService,
        JoinWaitlistService joinWaitlistService,
        LeaveWaitlistService leaveWaitlistService,
        IdempotencyKeyReader idempotencyKeyReader)
    {
        _createEnrollmentService = createEnrollmentService;
        _changeSeatsService = changeSeatsService;
        _cancelEnrollmentService = cancelEnrollmentService;
        _getEnrollmentService = getEnrollmentService;
        _joinWaitlistService = joinWaitlistService;
        _leaveWaitlistService = leaveWaitlistService;
        _idempotencyKeyReader = idempotencyKeyReader;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEnrollmentRequest request, CancellationToken ct)
    {
        var idempotencyKey = _idempotencyKeyReader.Read(Request);
        if (idempotencyKey is null)
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var command = new CreateEnrollmentCommand
        {
            UserId = userId,
            ClassId = request.ClassId,
            Seats = request.Seats,
            IdempotencyKey = idempotencyKey
        };
        var result = await _createEnrollmentService.CreateAsync(command, ct);
        return CreateEnrollmentResultToActionResultMapper.From(result);
    }

    [HttpPost("waitlist")]
    public async Task<IActionResult> JoinWaitlist([FromBody] JoinWaitlistRequest request, CancellationToken ct)
    {
        var idempotencyKey = _idempotencyKeyReader.Read(Request);
        if (idempotencyKey is null)
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var command = new JoinWaitlistCommand
        {
            UserId = userId,
            ClassId = request.ClassId,
            Seats = request.Seats,
            IdempotencyKey = idempotencyKey
        };
        var result = await _joinWaitlistService.JoinAsync(command, ct);
        return JoinWaitlistResultToActionResultMapper.From(result);
    }

    [HttpPost("waitlist/{id:long}/cancel")]
    public async Task<IActionResult> LeaveWaitlist(long id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var command = new LeaveWaitlistCommand
        {
            UserId = userId,
            EntryId = id
        };
        var result = await _leaveWaitlistService.LeaveAsync(command, ct);
        return LeaveWaitlistResultToActionResultMapper.From(result);
    }

    [HttpPost("{id:long}/seats")]
    public async Task<IActionResult> ChangeSeats(long id, [FromBody] ChangeSeatsRequest request, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var command = new ChangeSeatsCommand
        {
            UserId = userId,
            EnrollmentId = id,
            Seats = request.Seats
        };
        var result = await _changeSeatsService.ChangeAsync(command, ct);
        return ChangeSeatsResultToActionResultMapper.From(result);
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var command = new CancelEnrollmentCommand
        {
            UserId = userId,
            EnrollmentId = id
        };
        var result = await _cancelEnrollmentService.CancelAsync(command, ct);
        return CancelEnrollmentResultToActionResultMapper.From(result);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var command = new GetEnrollmentCommand
        {
            UserId = userId,
            EnrollmentId = id
        };
        var result = await _getEnrollmentService.GetAsync(command, ct);
        return GetEnrollmentResultToActionResultMapper.From(result);
    }
}
