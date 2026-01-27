using Microsoft.Extensions.Caching.Memory;
using Tindarr.Application.Abstractions.Caching;

namespace Tindarr.Infrastructure.Caching;

public sealed class MemoryOrDbTmdbCache(IMemoryCache memoryCache) : ITmdbCache
{
	public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
	{
		// In-memory implementation is synchronous. Cancellation is irrelevant.
		return ValueTask.FromResult(memoryCache.TryGetValue(key, out T? value) ? value : default);
	}

	public ValueTask SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
	{
		// In-memory implementation is synchronous. Cancellation is irrelevant.
		if (ttl <= TimeSpan.Zero)
		{
			return ValueTask.CompletedTask;
		}

		var opts = new MemoryCacheEntryOptions
		{
			AbsoluteExpirationRelativeToNow = ttl
		};

		memoryCache.Set(key, value, opts);
		return ValueTask.CompletedTask;
	}
}

