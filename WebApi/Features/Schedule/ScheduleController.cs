using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Schedule;
using WebApi.Controllers;
using WebApi.Helpers;
using WebApi.RateLimiting;

namespace WebApi.Features.Schedule;

[ApiController]
[Authorize]
[EnableRateLimiting(EnrollmentRateLimit.PolicyName)]
[Route("schedule")]
public class ScheduleController : BaseApiController
{
    private readonly GetScheduleService _getScheduleService;

    public ScheduleController(GetScheduleService getScheduleService)
    {
        _getScheduleService = getScheduleService;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] GetScheduleRequest request, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        if (request.From is not DateTime from || request.To is not DateTime to || to <= from)
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        var limit = request.Limit ?? SchedulePaginationLimits.DefaultPageSize;
        if (limit < SchedulePaginationLimits.MinPageSize || limit > SchedulePaginationLimits.MaxPageSize)
        {
            return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
        }

        ScheduleCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
        {
            if (!ScheduleContinuation.TryRead(request.ContinuationToken, out var cursorStartsAt, out var cursorClassId))
            {
                return ApiProblemsHelper.Create(StatusCodes.Status400BadRequest);
            }

            cursor = new ScheduleCursor(cursorStartsAt, cursorClassId);
        }

        var query = new GetScheduleQuery
        {
            UserId = userId,
            From = from,
            To = to,
            RoomId = request.RoomId,
            Limit = limit,
            Cursor = cursor
        };
        var page = await _getScheduleService.GetAsync(query, ct);
        return Ok(page);
    }
}
