using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Fetches and caches TMDB movie metadata (details, posters, providers).
/// Core-only: no rate limiting/retry/prefetch implementation yet.
/// </summary>
public sealed class TmdbMetadataWorker(ILogger<TmdbMetadataWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("TMDB metadata worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

