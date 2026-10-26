using System.Globalization;
using Common.Caching;

namespace BusinessLayer.Features.Enrollments;

public sealed class EnrollmentCardCache
{
    private readonly ICacheService _cache;

    public EnrollmentCardCache(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<EnrollmentCardLookup> ReadAsync(long userId, long enrollmentId, CancellationToken ct)
    {
        var generation = await GetGenerationAsync(userId, ct);
        var card = await _cache.GetAsync<EnrollmentCard>(CardKey(userId, generation, enrollmentId), ct);
        return new EnrollmentCardLookup(generation, card);
    }

    public async Task SetAsync(long userId, long generation, EnrollmentCard card, CancellationToken ct)
    {
        var current = await GetGenerationAsync(userId, ct);
        if (current != generation)
        {
            return;
        }

        await _cache.SetAsync(CardKey(userId, generation, card.Id), card, null, ct);
    }

    public Task InvalidateAsync(long userId, CancellationToken ct)
    {
        return _cache.IncrementAsync(GenerationKey(userId), ct);
    }

    private async Task<long> GetGenerationAsync(long userId, CancellationToken ct)
    {
        var generation = await _cache.ReadLongAsync(GenerationKey(userId), ct);
        return generation ?? 0;
    }

    private static string GenerationKey(long userId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"enrollments:user:{userId}:generation");
    }

    private static string CardKey(long userId, long generation, long enrollmentId)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"enrollments:user:{userId}:generation:{generation}:enrollment:{enrollmentId}");
    }
}

public sealed record EnrollmentCardLookup(long Generation, EnrollmentCard? Card);
