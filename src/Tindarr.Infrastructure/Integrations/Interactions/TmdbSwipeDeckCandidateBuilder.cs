using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Options;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Integrations.Interactions;

/// <summary>
/// Shared logic for building swipe deck candidates from local TMDB metadata and limited remote lookups.
/// Used by Emby and Jellyfin swipe deck sources to avoid duplication of limits, image URL rules, and lookup flow.
/// </summary>
public sealed class TmdbSwipeDeckCandidateBuilder(
	ITmdbMetadataStore metadataStore,
	ITmdbClient tmdbClient,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<TmdbSwipeDeckCandidateBuilder> logger)
{
	private const int CandidateLimit = 80;
	private const int DetailLookupBudget = 10;

	public async Task<IReadOnlyList<SwipeCard>> BuildCandidatesAsync(
		IReadOnlyList<int> candidateIds,
		string sourceName,
		CancellationToken cancellationToken)
	{
		var settings = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var tmdb = tmdbOptions.Value;
		var canLookup = tmdb.HasCredentials;

		var cards = new List<SwipeCard>(capacity: CandidateLimit);
		var lookupIds = new List<int>(capacity: DetailLookupBudget);

		foreach (var id in candidateIds)
		{
			if (cards.Count + lookupIds.Count >= CandidateLimit)
			{
				break;
			}

			var stored = await metadataStore.GetMovieAsync(id, cancellationToken).ConfigureAwait(false);
			if (stored is not null)
			{
				cards.Add(new SwipeCard(
					TmdbId: id,
					Title: (stored.Title ?? $"TMDB:{id}").Trim(),
					Overview: stored.Overview,
					PosterUrl: BuildImageUrl(settings, tmdb.ImageBaseUrl, stored.PosterPath, tmdb.PosterSize),
					BackdropUrl: BuildImageUrl(settings, tmdb.ImageBaseUrl, stored.BackdropPath, tmdb.BackdropSize),
					ReleaseYear: stored.ReleaseYear,
					Rating: stored.Rating));
				continue;
			}

			if (canLookup && lookupIds.Count < DetailLookupBudget)
			{
				lookupIds.Add(id);
				continue;
			}

			cards.Add(new SwipeCard(
				TmdbId: id,
				Title: $"TMDB:{id}",
				Overview: null,
				PosterUrl: null,
				BackdropUrl: null,
				ReleaseYear: null,
				Rating: null));
		}

		if (lookupIds.Count > 0)
		{
			var lookupTasks = lookupIds.Select(async id =>
			{
				MovieDetailsDto? details;
				try
				{
					details = await tmdbClient.GetMovieDetailsAsync(id, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
				{
					logger.LogDebug(ex, "tmdb details lookup failed for {SourceName} candidate. TmdbId={TmdbId}", sourceName, id);
					return new SwipeCard(
						TmdbId: id,
						Title: $"TMDB:{id}",
						Overview: null,
						PosterUrl: null,
						BackdropUrl: null,
						ReleaseYear: null,
						Rating: null);
				}

				if (details is null)
				{
					return new SwipeCard(
						TmdbId: id,
						Title: $"TMDB:{id}",
						Overview: null,
						PosterUrl: null,
						BackdropUrl: null,
						ReleaseYear: null,
						Rating: null);
				}

				try
				{
					await metadataStore.UpdateMovieDetailsAsync(details, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					logger.LogDebug(ex, "failed to persist tmdb details after {SourceName} swipedeck lookup. TmdbId={TmdbId}", sourceName, id);
				}

				return new SwipeCard(
					TmdbId: details.TmdbId,
					Title: details.Title,
					Overview: details.Overview,
					PosterUrl: details.PosterUrl,
					BackdropUrl: details.BackdropUrl,
					ReleaseYear: details.ReleaseYear,
					Rating: details.Rating);
			}).ToArray();

			var lookedUp = await Task.WhenAll(lookupTasks).ConfigureAwait(false);
			var lookedUpIds = new HashSet<int>();
			foreach (var c in lookedUp)
			{
				if (lookedUpIds.Add(c.TmdbId))
				{
					cards.Insert(0, c);
				}
			}
		}

		var seen = new HashSet<int>();
		return cards.Where(c => seen.Add(c.TmdbId)).ToList();
	}

	private static string? BuildImageUrl(TmdbMetadataSettings settings, string imageBaseUrl, string? path, string size)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var normalizedPath = path.Trim();
		if (normalizedPath.StartsWith('/'))
		{
			normalizedPath = normalizedPath.TrimStart('/');
		}

		var normalizedSize = (size ?? string.Empty).Trim().Trim('/');
		if (string.IsNullOrWhiteSpace(normalizedSize))
		{
			normalizedSize = "original";
		}

		if (settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0)
		{
			return $"/api/v1/tmdb/images/{normalizedSize}/{normalizedPath}";
		}

		var normalizedBase = (imageBaseUrl ?? string.Empty).TrimEnd('/');
		return $"{normalizedBase}/{normalizedSize}/{normalizedPath}";
	}
}
