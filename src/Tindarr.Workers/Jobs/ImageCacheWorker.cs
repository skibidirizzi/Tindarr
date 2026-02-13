using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Prefetches and caches poster/backdrop images; cleans expired image files.
/// Keeps the local TMDB image cache bounded.
/// </summary>
public sealed class ImageCacheWorker(ITmdbImageCache imageCache, ITmdbMetadataStore metadataStore, ILogger<ImageCacheWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(30);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		var settings = await metadataStore.GetSettingsAsync(stoppingToken).ConfigureAwait(false);
		var maxBytes = (long)Math.Max(0, settings.ImageCacheMaxMb) * 1024L * 1024L;
		await imageCache.PruneAsync(maxBytes, stoppingToken).ConfigureAwait(false);
	}
}

