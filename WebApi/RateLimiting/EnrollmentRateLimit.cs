using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace WebApi.RateLimiting;

public static class EnrollmentRateLimit
{
    public const string PolicyName = "enrollments";

    public const int PermitLimit = 60;

    private const int SegmentsPerWindow = 1;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Add(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                return new ValueTask(Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: ReasonPhrases.GetReasonPhrase(StatusCodes.Status429TooManyRequests))
                    .ExecuteAsync(context.HttpContext));
            };

            options.AddPolicy(PolicyName, httpContext =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                return RateLimitPartition.GetSlidingWindowLimiter(
                    userId,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit,
                        Window = Window,
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    });
            });
        });
    }
}
