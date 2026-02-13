using System.Net.Http;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Integrations.Plex;

public sealed class PlexSwipeDeckSource(
	IPlexService plexService,
	IUserPreferencesService preferencesService,
	ILogger<PlexSwipeDeckSource> logger) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken).ConfigureAwait(false);

		try
		{
			var library = await plexService.GetCachedLibraryAsync(scope, prefs, limit: 200, cancellationToken).ConfigureAwait(false);
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
