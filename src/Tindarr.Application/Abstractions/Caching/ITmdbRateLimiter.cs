namespace Tindarr.Application.Abstractions.Caching;

public interface ITmdbRateLimiter
{
	ValueTask WaitAsync(CancellationToken cancellationToken);
}

