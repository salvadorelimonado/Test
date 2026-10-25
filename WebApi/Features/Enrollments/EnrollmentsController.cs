using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Enrollments.Create;
using WebApi.Controllers;
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
    private readonly IdempotencyKeyReader _idempotencyKeyReader;

    public EnrollmentsController(
        CreateEnrollmentService createEnrollmentService,
        IdempotencyKeyReader idempotencyKeyReader)
    {
        _createEnrollmentService = createEnrollmentService;
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
}
