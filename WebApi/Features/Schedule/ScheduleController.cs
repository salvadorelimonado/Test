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
    private readonly GetScheduleService _getScheduleService;

    public ScheduleController(GetScheduleService getScheduleService)
    {
        _getScheduleService = getScheduleService;
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
            Cursor = request.Cursor
        };
        var listing = await _getScheduleService.GetAsync(query, ct);
        return Ok(ScheduleMapper.ToResponse(listing));
    }
}
