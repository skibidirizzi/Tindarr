using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Contracts.Movies;
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
	IRadarrClient radarrClient,
	IServiceSettingsRepository settingsRepo,
	IEffectiveAdvancedSettings effectiveAdvancedSettings,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<TmdbSwipeDeckSource> logger) : ISwipeDeckSource
{
	private const int RadarrLibraryPopulateLimit = 150;

	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		// When TMDB API/bearer not configured, serve discover from Radarr: populate tmdb-metadata from Radarr library, then serve cards from store.
		if (!effectiveAdvancedSettings.HasEffectiveTmdbCredentials())
		{
			return await GetCandidatesFromRadarrOrDemoAsync(userId, scope, cancellationToken).ConfigureAwait(false);
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

		var cards = BuildCardsFromPool(filteredPool.Take(200).ToList(), settings, tmdbOptions.Value);

		sw.Stop();
		if (cards.Count == 0)
		{
			logger.LogWarning(
				"tmdb deck source produced no candidates (pool or filtered pool empty). Falling back to demo deck. ServiceType={ServiceType} ServerId={ServerId} PoolCount={PoolCount} FilteredCount={FilteredCount} ElapsedMs={ElapsedMs}",
				scope.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				pool.Count,
				filteredPool.Count,
				sw.ElapsedMilliseconds);
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}
		logger.LogDebug(
			"tmdb deck source produced candidates. Source={Source} ServiceType={ServiceType} ServerId={ServerId} Count={Count} ElapsedMs={ElapsedMs}",
			"shared_pool",
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			cards.Count,
			sw.ElapsedMilliseconds);

		return cards;
	}

	/// <summary>When TMDB is not configured: populate tmdb-metadata from Radarr discover endpoint, then serve cards from store; otherwise demo deck.</summary>
	private async Task<IReadOnlyList<SwipeCard>> GetCandidatesFromRadarrOrDemoAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var radarrScope = await TryResolveDefaultRadarrScopeAsync(cancellationToken).ConfigureAwait(false);
		if (radarrScope is null)
		{
			logger.LogDebug("tmdb deck source: no TMDB and no Radarr, using demo deck.");
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}

		var settingsRecord = await settingsRepo.GetAsync(radarrScope, cancellationToken).ConfigureAwait(false);
		if (settingsRecord is null || string.IsNullOrWhiteSpace(settingsRecord.RadarrBaseUrl) || string.IsNullOrWhiteSpace(settingsRecord.RadarrApiKey))
		{
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}

		var connection = new RadarrConnection(settingsRecord.RadarrBaseUrl, settingsRecord.RadarrApiKey);
		var library = await radarrClient.GetLibraryAsync(connection, cancellationToken).ConfigureAwait(false);
		var discovered = new List<RadarrLookupMovie>();
		foreach (var lib in library.Take(RadarrLibraryPopulateLimit))
		{
			var lookup = await radarrClient.LookupMovieAsync(connection, lib.TmdbId, cancellationToken).ConfigureAwait(false);
			if (lookup is not null)
				discovered.Add(lookup);
		}

		if (discovered.Count == 0)
		{
			logger.LogDebug("tmdb deck source: Radarr library/discover returned no movies, using demo deck.");
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}

		var records = new List<TmdbDiscoverMovieRecord>(discovered.Count);
		var detailsList = new List<MovieDetailsDto>(discovered.Count);
		foreach (var m in discovered)
		{
			var dto = MapRadarrLookupToMovieDetailsDto(m);
			records.Add(new TmdbDiscoverMovieRecord(
				Id: m.TmdbId,
				Title: m.Title,
				OriginalTitle: null,
				Overview: null,
				PosterPath: TryExtractPathFromImageUrl(dto.PosterUrl),
				BackdropPath: TryExtractPathFromImageUrl(dto.BackdropUrl),
				ReleaseDate: dto.ReleaseDate,
				OriginalLanguage: null,
				VoteAverage: null,
				GenreIds: null,
				RuntimeMinutes: null));
			detailsList.Add(dto);
		}

		await metadataStore.UpsertMoviesAsync(records, cancellationToken).ConfigureAwait(false);
		foreach (var d in detailsList)
		{
			await metadataStore.UpdateMovieDetailsAsync(d, cancellationToken).ConfigureAwait(false);
		}

		var settings = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var pool = await metadataStore.ListDeckCandidatesAsync(take: 1_000, cancellationToken).ConfigureAwait(false);
		var cards = BuildCardsFromPool(pool.Take(200).ToList(), settings, tmdbOptions.Value);

		logger.LogDebug(
			"tmdb deck source produced candidates from Radarr discover. ServiceType={ServiceType} ServerId={ServerId} Count={Count} Discovered={Discovered}",
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			cards.Count,
			discovered.Count);

		return cards;
	}

	private static MovieDetailsDto MapRadarrLookupToMovieDetailsDto(RadarrLookupMovie m)
	{
		var posterUrl = GetPosterUrlFromLookup(m);
		var backdropUrl = GetBackdropUrlFromLookup(m);
		var releaseDate = m.Year.HasValue ? $"{m.Year:D4}-01-01" : null;
		return new MovieDetailsDto(
			TmdbId: m.TmdbId,
			Title: m.Title ?? $"TMDB:{m.TmdbId}",
			Overview: null,
			PosterUrl: posterUrl,
			BackdropUrl: backdropUrl,
			ReleaseDate: releaseDate,
			ReleaseYear: m.Year,
			MpaaRating: null,
			Rating: null,
			VoteCount: null,
			Genres: [],
			Regions: [],
			OriginalLanguage: null,
			RuntimeMinutes: null);
	}

	private static string? GetPosterUrlFromLookup(RadarrLookupMovie m)
	{
		var poster = m.Images?.FirstOrDefault(i => string.Equals(i.CoverType, "poster", StringComparison.OrdinalIgnoreCase));
		return poster?.RemoteUrl ?? poster?.Url ?? m.Images?.FirstOrDefault()?.RemoteUrl ?? m.Images?.FirstOrDefault()?.Url;
	}

	private static string? GetBackdropUrlFromLookup(RadarrLookupMovie m)
	{
		var backdrop = m.Images?.FirstOrDefault(i => string.Equals(i.CoverType, "backdrop", StringComparison.OrdinalIgnoreCase));
		return backdrop?.RemoteUrl ?? backdrop?.Url ?? m.Images?.LastOrDefault()?.RemoteUrl ?? m.Images?.LastOrDefault()?.Url;
	}

	private static string? TryExtractPathFromImageUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return null;
		var path = url.Trim();
		var q = path.IndexOf('?', StringComparison.Ordinal);
		if (q >= 0)
			path = path[..q];
		if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
			path = uri.AbsolutePath;
		else if (path.StartsWith("/", StringComparison.Ordinal))
			path = path.TrimStart('/');
		var tMarker = "/t/p/";
		var idx = path.IndexOf(tMarker, StringComparison.OrdinalIgnoreCase);
		if (idx >= 0)
		{
			var rest = path[(idx + tMarker.Length)..].TrimStart('/');
			var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
			return parts.Length >= 2 ? "/" + string.Join('/', parts.Skip(1)) : null;
		}
		var apiMarker = "/api/v1/tmdb/images/";
		idx = path.IndexOf(apiMarker, StringComparison.OrdinalIgnoreCase);
		if (idx >= 0)
		{
			var rest = path[(idx + apiMarker.Length)..].TrimStart('/');
			var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
			return parts.Length >= 2 ? "/" + string.Join('/', parts.Skip(1)) : null;
		}
		return path.StartsWith("/", StringComparison.Ordinal) ? path : null;
	}

	private static IReadOnlyList<SwipeCard> BuildCardsFromPool(IReadOnlyList<TmdbDiscoverMovieRecord> pool, TmdbMetadataSettings settings, TmdbOptions opts)
	{
		return pool.Select(m => new SwipeCard(
			TmdbId: m.Id,
			Title: (m.Title ?? m.OriginalTitle ?? $"TMDB:{m.Id}").Trim(),
			Overview: m.Overview,
			PosterUrl: BuildImageUrl(settings, opts.ImageBaseUrl, m.PosterPath, opts.PosterSize),
			BackdropUrl: BuildImageUrl(settings, opts.ImageBaseUrl, m.BackdropPath, opts.BackdropSize),
			ReleaseYear: TryParseYear(m.ReleaseDate),
			Rating: m.VoteAverage,
			RuntimeMinutes: m.RuntimeMinutes)).ToList();
	}

	private async Task<ServiceScope?> TryResolveDefaultRadarrScopeAsync(CancellationToken cancellationToken)
	{
		var rows = await settingsRepo.ListAsync(ServiceType.Radarr, cancellationToken).ConfigureAwait(false);
		var configured = rows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrBaseUrl))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrApiKey))
			.ToList();
		if (configured.Count == 0)
			return null;
		var preferred = configured.FirstOrDefault(x => string.Equals(x.ServerId, "default", StringComparison.OrdinalIgnoreCase)) ?? configured.First();
		return new ServiceScope(ServiceType.Radarr, preferred.ServerId);
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

