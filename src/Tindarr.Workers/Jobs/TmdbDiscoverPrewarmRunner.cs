using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Contracts.Tmdb;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Runs one pass of TMDB discover prewarm. Used by TmdbDiscoverPrewarmWorker and by setup complete.
/// </summary>
public sealed class TmdbDiscoverPrewarmRunner(
	IServiceScopeFactory scopeFactory,
	ITmdbCacheAdmin cacheAdmin,
	ILogger<TmdbDiscoverPrewarmRunner> logger) : ITmdbDiscoverPrewarmRunner
{
	public async Task<int> RunOnceAsync(int startOffset, CancellationToken cancellationToken)
	{
		_ = cacheAdmin.GetRowCountAsync(cancellationToken);

		const int batchSize = 10;
		using var scope = scopeFactory.CreateScope();
		var effectiveSettings = scope.ServiceProvider.GetRequiredService<IEffectiveAdvancedSettings>();
		if (!effectiveSettings.HasEffectiveTmdbCredentials())
		{
			logger.LogDebug("tmdb discover prewarm run (no effective credentials)");
			return startOffset;
		}

		var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
		var prefsService = scope.ServiceProvider.GetRequiredService<IUserPreferencesService>();
		var tmdbClient = scope.ServiceProvider.GetRequiredService<ITmdbClient>();
		var metadataStore = scope.ServiceProvider.GetRequiredService<ITmdbMetadataStore>();
		var imageCache = scope.ServiceProvider.GetRequiredService<ITmdbImageCache>();
		var tmdbOpts = scope.ServiceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
		var settings = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var stats = await metadataStore.GetStatsAsync(cancellationToken).ConfigureAwait(false);

		var moviesFull = stats.MovieCount >= settings.MaxMovies;
		var maxImageBytes = (long)Math.Max(0, settings.ImageCacheMaxMb) * 1024L * 1024L;
		var imageCacheEnabled = settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0;
		var imageCacheFull = imageCacheEnabled && await imageCache.GetTotalBytesAsync(cancellationToken).ConfigureAwait(false) >= maxImageBytes;

		if (moviesFull && (!imageCacheEnabled || imageCacheFull))
		{
			logger.LogDebug("tmdb prewarm skipped: cache full (movies={MovieCount}/{MaxMovies}, image cache {ImageStatus})", stats.MovieCount, settings.MaxMovies, imageCacheEnabled ? (imageCacheFull ? "at limit" : "filling") : "disabled");
			return startOffset;
		}

		var users = await userRepo.ListAsync(startOffset, batchSize, cancellationToken).ConfigureAwait(false);
		if (users.Count == 0)
		{
			return 0;
		}

		foreach (var user in users)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				IReadOnlyList<TmdbDiscoverMovieRecord>? discovered = null;

				if (!moviesFull)
				{
					var prefs = await prefsService.GetOrDefaultAsync(user.Id, cancellationToken).ConfigureAwait(false);
					var effectivePrefs = prefs;
					if (!string.IsNullOrWhiteSpace(settings.PrewarmOriginalLanguage))
					{
						effectivePrefs = effectivePrefs with
						{
							PreferredOriginalLanguages = new[] { settings.PrewarmOriginalLanguage }
						};
					}
					if (!string.IsNullOrWhiteSpace(settings.PrewarmRegion))
					{
						effectivePrefs = effectivePrefs with
						{
							PreferredRegions = new[] { settings.PrewarmRegion }
						};
					}

					discovered = await tmdbClient.DiscoverMoviesAsync(effectivePrefs, page: 1, limit: 50, cancellationToken).ConfigureAwait(false);
					await metadataStore.UpsertMoviesAsync(discovered, cancellationToken).ConfigureAwait(false);

					stats = await metadataStore.GetStatsAsync(cancellationToken).ConfigureAwait(false);
					moviesFull = stats.MovieCount >= settings.MaxMovies;
				}

				if (imageCacheEnabled)
				{
					if (!imageCacheFull)
					{
						var toPrefetch = discovered?.Take(8).ToList()
							?? (await metadataStore.ListDeckCandidatesAsync(take: 50, cancellationToken).ConfigureAwait(false)).Take(8).ToList();
						foreach (var m in toPrefetch)
						{
							if (imageCacheFull) break;
							if (!string.IsNullOrWhiteSpace(m.PosterPath))
								_ = await imageCache.GetOrFetchAsync(tmdbOpts.PosterSize, m.PosterPath!, cancellationToken).ConfigureAwait(false);
							if (!string.IsNullOrWhiteSpace(m.BackdropPath))
								_ = await imageCache.GetOrFetchAsync(tmdbOpts.BackdropSize, m.BackdropPath!, cancellationToken).ConfigureAwait(false);
							imageCacheFull = await imageCache.GetTotalBytesAsync(cancellationToken).ConfigureAwait(false) >= maxImageBytes;
						}
					}

					await imageCache.PruneAsync(maxImageBytes, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "tmdb discover prewarm failed for user {UserId}", user.Id);
			}
		}

		return startOffset + users.Count;
	}
}
