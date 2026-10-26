using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BusinessLayer.Features.Analytics;
using WebApi.Controllers;
using WebApi.RateLimiting;

namespace WebApi.Features.Analytics;

[ApiController]
[Authorize(Roles = BaseApiController.AdminRole)]
[EnableRateLimiting(ApiRateLimit.PolicyName)]
[Route("analytics")]
public class AnalyticsController : BaseApiController
{
    private readonly GetLoadService _getLoadService;

    public AnalyticsController(GetLoadService getLoadService)
    {
        _getLoadService = getLoadService;
    }

    [HttpGet("load")]
    public async Task<IActionResult> GetLoad([FromQuery] GetLoadRequest request, CancellationToken ct)
    {
        var from = request.From ?? throw new ArgumentNullException(nameof(request.From));
        var to = request.To ?? throw new ArgumentNullException(nameof(request.To));

        var report = await _getLoadService.GetAsync(
            new GetLoadQuery
            {
                From = from,
                To = to,
                RoomId = request.RoomId
            },
            ct);
        return Ok(report);
    }
}
