using BusinessLayer.Features.Enrollments;
using Microsoft.Extensions.Caching.Hybrid;
using Shouldly;

namespace BusinessLayer.Tests;

[TestClass]
public class EnrollmentCardCacheTests
{
    [TestMethod]
    public async Task Invalidate_WhenTheCacheThrows_DoesNotPropagateTheFailure()
    {
        // Arrange
        var userId = 7L;
        var cache = new EnrollmentCardCache(new ThrowingHybridCache());

        // Act
        var act = () => cache.InvalidateAsync(userId);

        // Assert
        await Should.NotThrowAsync(act);
    }

    private sealed class ThrowingHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis is down.");
        }

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis is down.");
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis is down.");
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis is down.");
        }

        public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis is down.");
        }
    }
}
