namespace Tindarr.Application.Abstractions.Caching;

public sealed record TmdbImageCacheResult(string FilePath, string ContentType);

public interface ITmdbImageCache
{
	Task<TmdbImageCacheResult?> GetOrFetchAsync(string size, string path, CancellationToken cancellationToken);

	Task<bool> HasAsync(string size, string path, CancellationToken cancellationToken);

	ValueTask<long> GetTotalBytesAsync(CancellationToken cancellationToken);

	Task PruneAsync(long maxBytes, CancellationToken cancellationToken);
}
