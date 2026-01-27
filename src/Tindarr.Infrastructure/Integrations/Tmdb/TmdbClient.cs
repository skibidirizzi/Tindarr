using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Integrations.Tmdb;

public sealed class TmdbClient(HttpClient httpClient, IOptions<TmdbOptions> options, ILogger<TmdbClient> logger) : ITmdbClient
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private readonly TmdbOptions _tmdb = options.Value;

	public async Task<IReadOnlyList<SwipeCard>> DiscoverAsync(
		UserPreferencesRecord preferences,
		int page,
		int limit,
		CancellationToken cancellationToken)
	{
		if (!_tmdb.HasCredentials)
		{
			return [];
		}

		page = Math.Clamp(page, 1, 500);
		limit = Math.Clamp(limit, 1, 200);

		var cards = new List<SwipeCard>(capacity: limit);
		var currentPage = page;

		while (cards.Count < limit && currentPage <= page + 4)
		{
			var uri = BuildDiscoverUri(preferences, currentPage);
			using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				break;
			}

			if (!response.IsSuccessStatusCode)
			{
				// Typical causes: bad key (401/403), rate limiting (429), transient upstream issues.
				var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				if (errorBody.Length > 800) { errorBody = errorBody[..800]; }

				logger.LogWarning(
					"TMDB discover failed. Status={Status} Uri={Uri} Body={Body}",
					(int)response.StatusCode,
					RedactSensitiveQuery(uri),
					errorBody);
				break;
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			TmdbDiscoverResponse? parsed;
			try
			{
				parsed = JsonSerializer.Deserialize<TmdbDiscoverResponse>(body, Json);
			}
			catch (JsonException ex)
			{
				logger.LogWarning(ex, "TMDB discover returned invalid JSON. Uri={Uri}", uri);
				break;
			}

			if (parsed?.Results is { Count: > 0 })
			{
				foreach (var movie in parsed.Results)
				{
					cards.Add(MapSwipeCard(movie));
					if (cards.Count >= limit)
					{
						break;
					}
				}
			}

			// Stop if the API indicates no more pages.
			if (parsed is null || currentPage >= parsed.TotalPages)
			{
				break;
			}

			currentPage++;
		}

		return cards;
	}

	public async Task<MovieDetailsDto?> GetMovieDetailsAsync(int tmdbId, CancellationToken cancellationToken)
	{
		if (tmdbId <= 0)
		{
			return null;
		}

		if (!_tmdb.HasCredentials)
		{
			return null;
		}

		var uri = AppendCredentials($"movie/{tmdbId}");
		using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		if (!response.IsSuccessStatusCode)
		{
			var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (errorBody.Length > 800) { errorBody = errorBody[..800]; }

			logger.LogWarning(
				"TMDB movie details failed. Status={Status} TmdbId={TmdbId} Uri={Uri} Body={Body}",
				(int)response.StatusCode,
				tmdbId,
				RedactSensitiveQuery(uri),
				errorBody);
			return null;
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		TmdbMovieDetails? parsed;
		try
		{
			parsed = JsonSerializer.Deserialize<TmdbMovieDetails>(body, Json);
		}
		catch (JsonException ex)
		{
			logger.LogWarning(ex, "TMDB movie details returned invalid JSON. TmdbId={TmdbId} Uri={Uri}", tmdbId, uri);
			return null;
		}
		if (parsed is null)
		{
			return null;
		}

		var releaseYear = TryParseYear(parsed.ReleaseDate);

		return new MovieDetailsDto(
			TmdbId: parsed.Id,
			Title: parsed.Title ?? parsed.OriginalTitle ?? $"TMDB:{parsed.Id}",
			Overview: parsed.Overview,
			PosterUrl: BuildImageUrl(parsed.PosterPath, _tmdb.PosterSize),
			BackdropUrl: BuildImageUrl(parsed.BackdropPath, _tmdb.BackdropSize),
			ReleaseDate: parsed.ReleaseDate,
			ReleaseYear: releaseYear,
			Rating: parsed.VoteAverage,
			VoteCount: parsed.VoteCount,
			Genres: parsed.Genres?.Where(g => !string.IsNullOrWhiteSpace(g.Name)).Select(g => g.Name!).ToList() ?? [],
			OriginalLanguage: parsed.OriginalLanguage,
			RuntimeMinutes: parsed.Runtime);
	}

	private string BuildDiscoverUri(UserPreferencesRecord preferences, int page)
	{
		var qs = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["include_adult"] = preferences.IncludeAdult ? "true" : "false",
			["sort_by"] = string.IsNullOrWhiteSpace(preferences.SortBy) ? "popularity.desc" : preferences.SortBy,
			["page"] = page.ToString()
		};

		if (preferences.MinReleaseYear is { } minYear)
		{
			qs["primary_release_date.gte"] = $"{minYear:0000}-01-01";
		}

		if (preferences.MaxReleaseYear is { } maxYear)
		{
			qs["primary_release_date.lte"] = $"{maxYear:0000}-12-31";
		}

		if (preferences.MinRating is { } minRating)
		{
			qs["vote_average.gte"] = minRating.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		if (preferences.MaxRating is { } maxRating)
		{
			qs["vote_average.lte"] = maxRating.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		if (preferences.PreferredGenres is { Count: > 0 })
		{
			qs["with_genres"] = string.Join("|", preferences.PreferredGenres);
		}

		if (preferences.ExcludedGenres is { Count: > 0 })
		{
			qs["without_genres"] = string.Join("|", preferences.ExcludedGenres);
		}

		if (preferences.PreferredOriginalLanguages is { Count: > 0 })
		{
			// TMDB expects a single language code for discover.
			qs["with_original_language"] = preferences.PreferredOriginalLanguages[0];
		}

		if (preferences.PreferredRegions is { Count: > 0 })
		{
			// Region is used for release date filtering and results localization.
			qs["region"] = preferences.PreferredRegions[0];
		}

		return AppendCredentials("discover/movie", qs);
	}

	private string AppendCredentials(string path, IReadOnlyDictionary<string, string?>? query = null)
	{
		var parts = new List<string>(capacity: (query?.Count ?? 0) + 1);

		// If a v3 api key is configured, include it (legacy / compatible path).
		// If we're using Bearer token auth (ReadAccessToken), we intentionally avoid query-string secrets.
		if (!string.IsNullOrWhiteSpace(_tmdb.ApiKey))
		{
			parts.Add($"api_key={Uri.EscapeDataString(_tmdb.ApiKey)}");
		}

		if (query is not null)
		{
			foreach (var (k, v) in query)
			{
				if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v))
				{
					continue;
				}

				parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}");
			}
		}

		return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
	}

	private static string RedactSensitiveQuery(string uri)
	{
		if (string.IsNullOrWhiteSpace(uri) || !uri.Contains('?', StringComparison.Ordinal))
		{
			return uri;
		}

		var idx = uri.IndexOf('?', StringComparison.Ordinal);
		if (idx < 0 || idx == uri.Length - 1)
		{
			return uri;
		}

		var path = uri[..(idx + 1)];
		var query = uri[(idx + 1)..];
		var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			return uri;
		}

		static bool IsSensitiveKey(string key) =>
			key.Equals("api_key", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("apikey", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("token", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("access_token", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("refresh_token", StringComparison.OrdinalIgnoreCase);

		for (var i = 0; i < parts.Length; i++)
		{
			var p = parts[i];
			var eq = p.IndexOf('=');
			var k = eq < 0 ? p : p[..eq];
			if (IsSensitiveKey(k))
			{
				parts[i] = k + "=[REDACTED]";
			}
		}

		return path + string.Join("&", parts);
	}

	private SwipeCard MapSwipeCard(TmdbDiscoverMovie movie)
	{
		return new SwipeCard(
			TmdbId: movie.Id,
			Title: movie.Title ?? movie.OriginalTitle ?? $"TMDB:{movie.Id}",
			Overview: movie.Overview,
			PosterUrl: BuildImageUrl(movie.PosterPath, _tmdb.PosterSize),
			BackdropUrl: BuildImageUrl(movie.BackdropPath, _tmdb.BackdropSize),
			ReleaseYear: TryParseYear(movie.ReleaseDate),
			Rating: movie.VoteAverage);
	}

	private string? BuildImageUrl(string? path, string size)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var normalizedBase = _tmdb.ImageBaseUrl.TrimEnd('/');
		var normalizedSize = size.Trim('/');
		var normalizedPath = path.StartsWith('/') ? path : "/" + path;
		return $"{normalizedBase}/{normalizedSize}{normalizedPath}";
	}

	private static int? TryParseYear(string? releaseDate)
	{
		if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
		{
			return null;
		}

		return int.TryParse(releaseDate.AsSpan(0, 4), out var year) ? year : null;
	}

	private sealed record TmdbDiscoverResponse(
		[property: JsonPropertyName("page")] int Page,
		[property: JsonPropertyName("total_pages")] int TotalPages,
		[property: JsonPropertyName("results")] List<TmdbDiscoverMovie> Results);

	private sealed record TmdbDiscoverMovie(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("title")] string? Title,
		[property: JsonPropertyName("original_title")] string? OriginalTitle,
		[property: JsonPropertyName("overview")] string? Overview,
		[property: JsonPropertyName("poster_path")] string? PosterPath,
		[property: JsonPropertyName("backdrop_path")] string? BackdropPath,
		[property: JsonPropertyName("release_date")] string? ReleaseDate,
		[property: JsonPropertyName("vote_average")] double? VoteAverage);

	private sealed record TmdbMovieDetails(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("title")] string? Title,
		[property: JsonPropertyName("original_title")] string? OriginalTitle,
		[property: JsonPropertyName("overview")] string? Overview,
		[property: JsonPropertyName("poster_path")] string? PosterPath,
		[property: JsonPropertyName("backdrop_path")] string? BackdropPath,
		[property: JsonPropertyName("release_date")] string? ReleaseDate,
		[property: JsonPropertyName("vote_average")] double? VoteAverage,
		[property: JsonPropertyName("vote_count")] int? VoteCount,
		[property: JsonPropertyName("original_language")] string? OriginalLanguage,
		[property: JsonPropertyName("runtime")] int? Runtime,
		[property: JsonPropertyName("genres")] List<TmdbGenre>? Genres);

	private sealed record TmdbGenre(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("name")] string? Name);
}

