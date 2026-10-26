using System.Globalization;
using DataAccessLayer.Repositories;
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

    public async Task<EnrollmentCardRow?> GetOrLoadAsync(
        long userId,
        long enrollmentId,
        Func<CancellationToken, Task<EnrollmentCardRow?>> load,
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
                // решение: локальный кэш не сбрасывается на других экземплярах, поэтому карточку читаем только из Redis.
                Flags = HybridCacheEntryFlags.DisableLocalCache
            },
            tags: [UserTag(userId)],
            cancellationToken: ct);

        return stored.Card;
    }

    public async Task InvalidateAsync(long userId)
    {
        try
        {
            await _cache.RemoveByTagAsync(UserTag(userId), CancellationToken.None);
        }
        catch (Exception)
        {
            // решение: сброс идёт после коммита и без токена запроса. Сбой Redis не отменяет уже сохранённую операцию.
        }
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
        public EnrollmentCardRow? Card { get; set; }
    }
}
