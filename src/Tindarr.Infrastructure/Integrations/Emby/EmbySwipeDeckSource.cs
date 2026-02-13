using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Integrations.Emby;

public sealed class EmbySwipeDeckSource(
	ILibraryCacheRepository libraryCache,
	ITmdbClient tmdbClient,
	ILogger<EmbySwipeDeckSource> logger) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var ids = await libraryCache.GetTmdbIdsAsync(scope, cancellationToken).ConfigureAwait(false);
		if (ids.Count == 0)
		{
			throw new InvalidOperationException("Emby library is not synced yet. Sync the Emby library in Admin Console, then try again.");
		}

		var candidateIds = ids.Take(80).ToList();
		var cards = new List<SwipeCard>(capacity: candidateIds.Count);

		foreach (var id in candidateIds)
		{
			MovieDetailsDto? details;
			try
			{
				details = await tmdbClient.GetMovieDetailsAsync(id, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				logger.LogDebug(ex, "tmdb details lookup failed for emby candidate. TmdbId={TmdbId}", id);
				continue;
			}

			if (details is null)
			{
				continue;
			}

			cards.Add(new SwipeCard(
				TmdbId: details.TmdbId,
				Title: details.Title,
				Overview: details.Overview,
				PosterUrl: details.PosterUrl,
				BackdropUrl: details.BackdropUrl,
				ReleaseYear: details.ReleaseYear,
				Rating: details.Rating));
		}

		return cards;
	}
}
