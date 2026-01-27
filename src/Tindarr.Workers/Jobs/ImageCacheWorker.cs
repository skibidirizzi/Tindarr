using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Prefetches and caches poster/backdrop images; cleans expired image files.
/// Core-only: no image storage/caching implementation yet.
/// </summary>
public sealed class ImageCacheWorker(ILogger<ImageCacheWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(30);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Image cache worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

