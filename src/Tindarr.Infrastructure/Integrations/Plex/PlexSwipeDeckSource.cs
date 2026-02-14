using System.Net.Http;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Integrations.Plex;

public sealed class PlexSwipeDeckSource(
	IPlexService plexService,
	IPlexDeckIndexRepository deckIndex,
	IServiceSettingsRepository settingsRepo,
	IPlexLibraryCacheRepository plexLibraryCache,
	IUserPreferencesService preferencesService,
	ILogger<PlexSwipeDeckSource> logger) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken).ConfigureAwait(false);

		try
		{
			// Fast path: query the precomputed deck index in plexcache.db.
			var indexed = await deckIndex.QueryAsync(scope, prefs, limit: 250, cancellationToken).ConfigureAwait(false);
			if (indexed.Count > 0)
			{
				return indexed;
			}

			// Fallback path: on-demand TMDB lookups through PlexService.
			var library = await plexService.GetCachedLibraryAsync(scope, prefs, limit: 200, cancellationToken).ConfigureAwait(false);
			if (library.Count == 0)
			{
				// Plex swipedeck relies on a cached library sync; if this is a fresh install, kick off a sync once.
				await plexService.EnsureLibrarySyncAsync(scope, cancellationToken).ConfigureAwait(false);
				library = await plexService.GetCachedLibraryAsync(scope, prefs, limit: 200, cancellationToken).ConfigureAwait(false);
				if (library.Count == 0)
				{
					var cachedIds = await plexLibraryCache.GetTmdbIdsAsync(scope, cancellationToken).ConfigureAwait(false);
					var settings = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
					var lastSync = settings?.PlexLastLibrarySyncUtc;

					if (cachedIds.Count == 0)
					{
						if (lastSync is not null)
						{
							throw new InvalidOperationException(
								$"Plex library sync ran at {lastSync:O}, but no TMDB IDs were cached for this server. " +
								"This usually means Plex did not provide TMDB GUIDs for your movies. " +
								"In Plex, refresh metadata / ensure your agent includes TMDB IDs, then sync again in Admin Console → Plex.");
						}

						throw new InvalidOperationException(
							"Plex library is not synced yet. Sync the Plex library in Admin Console, then try again.");
					}

					throw new InvalidOperationException(
						$"Plex library has {cachedIds.Count} cached TMDB IDs, but no swipe cards could be built yet. " +
						"Fetch details in Admin Console → Plex → Cache contents → Fetch all details, then try again.");
				}
			}

			return library
				.Select(m => new SwipeCard(
					TmdbId: m.TmdbId,
					Title: m.Title,
					Overview: m.Overview,
					PosterUrl: m.PosterUrl,
					BackdropUrl: m.BackdropUrl,
					ReleaseYear: m.ReleaseYear,
					Rating: m.Rating))
				.ToList();
		}
		catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			logger.LogWarning(ex, "plex swipedeck timed out. ServerId={ServerId}", scope.ServerId);
			throw new HttpRequestException("Plex did not respond (request timed out).", ex);
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "plex swipedeck request failed. ServerId={ServerId}", scope.ServerId);
			throw;
		}
	}
}
