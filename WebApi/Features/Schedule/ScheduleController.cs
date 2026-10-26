using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Schedule;
using WebApi.Controllers;
using WebApi.RateLimiting;

namespace WebApi.Features.Schedule;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimit.PolicyName)]
[Route("schedule")]
public class ScheduleController : BaseApiController
{
    private readonly GetScheduleHandler _getScheduleHandler;

    public ScheduleController(GetScheduleHandler getScheduleHandler)
    {
        _getScheduleHandler = getScheduleHandler;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] GetScheduleRequest request, CancellationToken ct)
    {
        var from = request.From ?? throw new ArgumentNullException(nameof(request.From));
        var to = request.To ?? throw new ArgumentNullException(nameof(request.To));
        var query = new GetScheduleQuery
        {
            UserId = CurrentUserId,
            From = from,
            To = to,
            RoomId = request.RoomId,
            Limit = request.Limit ?? SchedulePaginationLimits.DefaultPageSize,
            Cursor = request.ContinuationToken
        };
        var page = await _getScheduleHandler.GetAsync(query, ct);
        return Ok(ScheduleMapper.ToResponse(page));
    }
}
