namespace Tindarr.Application.Abstractions.Caching;

public interface ITmdbCache
{
	ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

	ValueTask SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken);
}

