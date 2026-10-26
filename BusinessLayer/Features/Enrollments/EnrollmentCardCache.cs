using System.Globalization;
using Microsoft.Extensions.Caching.Hybrid;

namespace BusinessLayer.Features.Enrollments;

public sealed class EnrollmentCardCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly HybridCache _cache;

    public EnrollmentCardCache(HybridCache cache)
    {
        _cache = cache;
    }

    public async Task<EnrollmentCard?> GetOrLoadAsync(
        long userId,
        long enrollmentId,
        Func<CancellationToken, Task<EnrollmentCard?>> load,
        CancellationToken ct)
    {
        var stored = await _cache.GetOrCreateAsync(
            CardKey(userId, enrollmentId),
            load,
            async (loader, token) => new StoredEnrollmentCard
            {
                Card = await loader(token)
            },
            options: new HybridCacheEntryOptions
            {
                Expiration = Lifetime,
                LocalCacheExpiration = Lifetime
            },
            tags: [UserTag(userId)],
            cancellationToken: ct);

        return stored.Card;
    }

    public ValueTask InvalidateAsync(long userId, CancellationToken ct)
    {
        return _cache.RemoveByTagAsync(UserTag(userId), ct);
    }

    private static string CardKey(long userId, long enrollmentId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"enrollment-card:{userId}:{enrollmentId}");
    }

    private static string UserTag(long userId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"enrollment-card-user:{userId}");
    }

    private sealed class StoredEnrollmentCard
    {
        public EnrollmentCard? Card { get; set; }
    }
}
