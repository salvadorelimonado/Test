using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using RedisRateLimiting;
using StackExchange.Redis;

namespace WebApi.RateLimiting;

public static class ApiRateLimit
{
    public const string PolicyName = "api";

    public const int PermitLimit = 60;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddApiRateLimit(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                return new ValueTask(Microsoft.AspNetCore.Http.Results.Problem(
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

                var redis = httpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                // решение: при недоступном Redis лимитер отвечает ошибкой, а не пропускает запрос без ограничения.
                return RedisRateLimitPartition.GetSlidingWindowRateLimiter(
                    userId,
                    _ => new RedisSlidingWindowRateLimiterOptions
                    {
                        ConnectionMultiplexerFactory = () => redis,
                        PermitLimit = PermitLimit,
                        Window = Window
                    });
            });
        });

        return services;
    }
}
