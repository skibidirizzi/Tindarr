using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Options;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Gradually enriches locally stored TMDB movies with full details (genres, ratings, etc.).
/// Uses the existing TMDB HTTP caching + rate limiting pipeline.
/// </summary>
public sealed class TmdbDetailsBackfillWorker(
	IServiceScopeFactory scopeFactory,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<TmdbDetailsBackfillWorker> logger) : PeriodicBackgroundService(logger)
{
	private readonly TmdbOptions _tmdb = tmdbOptions.Value;

	protected override TimeSpan Interval => TimeSpan.FromSeconds(60);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		if (!_tmdb.HasCredentials)
		{
			Logger.LogDebug("tmdb details backfill tick (no credentials)");
			return;
		}

		using var scope = scopeFactory.CreateScope();
		var tmdbClient = scope.ServiceProvider.GetRequiredService<ITmdbClient>();
		var metadataStore = scope.ServiceProvider.GetRequiredService<ITmdbMetadataStore>();

		var ids = await metadataStore.ListMoviesNeedingDetailsAsync(limit: 10, stoppingToken).ConfigureAwait(false);
		if (ids.Count == 0)
		{
			return;
		}

		foreach (var id in ids)
		{
			stoppingToken.ThrowIfCancellationRequested();
			try
			{
				var details = await tmdbClient.GetMovieDetailsAsync(id, stoppingToken).ConfigureAwait(false);
				if (details is null)
				{
					continue;
				}

				await metadataStore.UpdateMovieDetailsAsync(details, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex, "tmdb details backfill failed for tmdbId {TmdbId}", id);
			}
		}
	}
}
