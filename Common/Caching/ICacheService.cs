namespace Common.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    Task<long?> ReadLongAsync(string key, CancellationToken ct = default);

    Task<long> IncrementAsync(string key, CancellationToken ct = default);
}
