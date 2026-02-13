namespace Tindarr.Application.Abstractions.Caching;

public interface ITmdbCacheAdmin
{
	ValueTask<int> GetMaxRowsAsync(CancellationToken cancellationToken);

	ValueTask SetMaxRowsAsync(int maxRows, CancellationToken cancellationToken);

	ValueTask<int> GetRowCountAsync(CancellationToken cancellationToken);
}
