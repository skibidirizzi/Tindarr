using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Periodically pre-warms TMDB discover responses into the shared sqlite cache.
/// This keeps swipe-deck calls fast and reduces the chance of user-facing rate-limit waits.
/// </summary>
public sealed class TmdbDiscoverPrewarmWorker(
	IServiceScopeFactory scopeFactory,
	ITmdbCacheAdmin cacheAdmin,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<TmdbDiscoverPrewarmWorker> logger) : PeriodicBackgroundService(logger)
{
	private readonly TmdbOptions _tmdb = tmdbOptions.Value;
	private int _skip;

	protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

	protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(5);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		if (!_tmdb.HasCredentials)
		{
			Logger.LogDebug("tmdb discover prewarm tick (no credentials)");
			return;
		}

		// Keep the old HTTP cache bounded, but don't block metadata pool fill on it.
		_ = cacheAdmin.GetRowCountAsync(stoppingToken);

		const int batchSize = 10;
		using var scope = scopeFactory.CreateScope();
		var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
		var prefsService = scope.ServiceProvider.GetRequiredService<IUserPreferencesService>();
		var tmdbClient = scope.ServiceProvider.GetRequiredService<ITmdbClient>();
		var metadataStore = scope.ServiceProvider.GetRequiredService<ITmdbMetadataStore>();
		var imageCache = scope.ServiceProvider.GetRequiredService<ITmdbImageCache>();
		var tmdbOpts = scope.ServiceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
		var settings = await metadataStore.GetSettingsAsync(stoppingToken).ConfigureAwait(false);

		var users = await userRepo.ListAsync(_skip, batchSize, stoppingToken).ConfigureAwait(false);
		if (users.Count == 0)
		{
			_skip = 0;
			return;
		}

		foreach (var user in users)
		{
			stoppingToken.ThrowIfCancellationRequested();
			try
			{
				var prefs = await prefsService.GetOrDefaultAsync(user.Id, stoppingToken).ConfigureAwait(false);
				var discovered = await tmdbClient.DiscoverMoviesAsync(prefs, page: 1, limit: 50, stoppingToken).ConfigureAwait(false);
				await metadataStore.AddToUserPoolAsync(user.Id, discovered, stoppingToken).ConfigureAwait(false);

				if (settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0)
				{
					// Best-effort: prefetch a small number of images per tick.
					var maxBytes = (long)settings.ImageCacheMaxMb * 1024L * 1024L;
					var toPrefetch = discovered.Take(8).ToList();
					foreach (var m in toPrefetch)
					{
						if (!string.IsNullOrWhiteSpace(m.PosterPath))
						{
							_ = await imageCache.GetOrFetchAsync(tmdbOpts.PosterSize, m.PosterPath!, stoppingToken).ConfigureAwait(false);
						}
						if (!string.IsNullOrWhiteSpace(m.BackdropPath))
						{
							_ = await imageCache.GetOrFetchAsync(tmdbOpts.BackdropSize, m.BackdropPath!, stoppingToken).ConfigureAwait(false);
						}
					}

					await imageCache.PruneAsync(maxBytes, stoppingToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex, "tmdb discover prewarm failed for user {UserId}", user.Id);
			}
		}

		_skip += users.Count;
	}
}
