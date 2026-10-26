using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Enrollments.Cancel;
using BusinessLayer.Features.Enrollments.ChangeSeats;
using BusinessLayer.Features.Enrollments.Create;
using WebApi.Controllers;
using WebApi.Features.Enrollments.Cancel;
using WebApi.Features.Enrollments.ChangeSeats;
using WebApi.Features.Enrollments.Create;
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
    private readonly IdempotencyKeyReader _idempotencyKeyReader;

    public EnrollmentsController(
        CreateEnrollmentService createEnrollmentService,
        ChangeSeatsService changeSeatsService,
        CancelEnrollmentService cancelEnrollmentService,
        IdempotencyKeyReader idempotencyKeyReader)
    {
        _createEnrollmentService = createEnrollmentService;
        _changeSeatsService = changeSeatsService;
        _cancelEnrollmentService = cancelEnrollmentService;
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
}
