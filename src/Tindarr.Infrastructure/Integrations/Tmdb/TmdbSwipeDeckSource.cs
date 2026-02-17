using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Tindarr.Infrastructure.Integrations.Tmdb;

public sealed class TmdbSwipeDeckSource(
	ITmdbClient tmdbClient,
	IUserPreferencesService preferencesService,
	ITmdbMetadataStore metadataStore,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<TmdbSwipeDeckSource> logger) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		// If TMDB isn't configured yet (public MSI install), fall back to the demo deck.
		if (!tmdbOptions.Value.HasCredentials)
		{
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}

		var sw = Stopwatch.StartNew();
		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken).ConfigureAwait(false);

		// Use the shared metadata pool and filter by current prefs at deck build time.
		var settings = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var pool = await metadataStore.ListDeckCandidatesAsync(take: 1_000, cancellationToken).ConfigureAwait(false);
		var filteredPool = pool.Where(m => MatchesPreferencesLoosely(m, prefs)).ToList();

		// If the filtered pool is low, pull more from TMDB Discover for this user's prefs and upsert into the shared store.
		if (filteredPool.Count < 25)
		{
			var discovered = await tmdbClient.DiscoverMoviesAsync(prefs, page: 1, limit: 200, cancellationToken).ConfigureAwait(false);
			await metadataStore.UpsertMoviesAsync(discovered, cancellationToken).ConfigureAwait(false);
			pool = await metadataStore.ListDeckCandidatesAsync(take: 1_000, cancellationToken).ConfigureAwait(false);
			filteredPool = pool.Where(m => MatchesPreferencesLoosely(m, prefs)).ToList();
		}

		var cards = filteredPool.Take(200).Select(m => new SwipeCard(
			TmdbId: m.Id,
			Title: (m.Title ?? m.OriginalTitle ?? $"TMDB:{m.Id}").Trim(),
			Overview: m.Overview,
			PosterUrl: BuildImageUrl(settings, tmdbOptions.Value.ImageBaseUrl, m.PosterPath, tmdbOptions.Value.PosterSize),
			BackdropUrl: BuildImageUrl(settings, tmdbOptions.Value.ImageBaseUrl, m.BackdropPath, tmdbOptions.Value.BackdropSize),
			ReleaseYear: TryParseYear(m.ReleaseDate),
			Rating: m.VoteAverage)).ToList();

		sw.Stop();
		logger.LogDebug(
			"tmdb deck source produced candidates. Source={Source} ServiceType={ServiceType} ServerId={ServerId} Count={Count} ElapsedMs={ElapsedMs}",
			"shared_pool",
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			cards.Count,
			sw.ElapsedMilliseconds);

		return cards;
	}

	private static bool MatchesPreferencesLoosely(TmdbDiscoverMovieRecord movie, UserPreferencesRecord preferences)
	{
		var year = TryParseYear(movie.ReleaseDate);
		if (preferences.MinReleaseYear is not null && year is not null && year < preferences.MinReleaseYear)
		{
			return false;
		}
		if (preferences.MaxReleaseYear is not null && year is not null && year > preferences.MaxReleaseYear)
		{
			return false;
		}

		if (preferences.MinRating is not null && movie.VoteAverage is not null && movie.VoteAverage < preferences.MinRating)
		{
			return false;
		}
		if (preferences.MaxRating is not null && movie.VoteAverage is not null && movie.VoteAverage > preferences.MaxRating)
		{
			return false;
		}

		if (preferences.PreferredOriginalLanguages is { Count: > 0 })
		{
			if (!string.IsNullOrWhiteSpace(movie.OriginalLanguage)
				&& !preferences.PreferredOriginalLanguages.Contains(movie.OriginalLanguage, StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		if (preferences.ExcludedOriginalLanguages is { Count: > 0 } && !string.IsNullOrWhiteSpace(movie.OriginalLanguage))
		{
			if (preferences.ExcludedOriginalLanguages.Contains(movie.OriginalLanguage, StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		if (preferences.PreferredGenres is { Count: > 0 } && movie.GenreIds is { Count: > 0 })
		{
			if (!preferences.PreferredGenres.Any(g => movie.GenreIds.Contains(g)))
			{
				return false;
			}
		}

		if (preferences.ExcludedGenres is { Count: > 0 } && movie.GenreIds is { Count: > 0 })
		{
			if (preferences.ExcludedGenres.Any(g => movie.GenreIds.Contains(g)))
			{
				return false;
			}
		}

		// Regions require details-derived data; the discover record doesn't include regions.
		// Keep this loose to avoid filtering out movies until details are available.
		return true;
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

	private static int? TryParseYear(string? releaseDate)
	{
		if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
		{
			return null;
		}

		return int.TryParse(releaseDate.AsSpan(0, 4), out var year) ? year : null;
	}
}

