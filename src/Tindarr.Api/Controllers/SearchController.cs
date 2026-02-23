using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;
using Tindarr.Contracts.Search;
using Tindarr.Domain.Common;
using Microsoft.Extensions.Options;

namespace Tindarr.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
[Authorize]
public sealed class SearchController(
	IServiceSettingsRepository settingsRepo,
	IRadarrClient radarrClient,
	ITmdbMetadataStore metadataStore,
	ITmdbClient tmdbClient,
	IOptions<TmdbOptions> tmdbOptions) : ControllerBase
{
	/// <summary>Search movies by title. If Radarr is configured, uses Radarr as passthrough; otherwise local cache first, then TMDB API.</summary>
	[HttpGet("movies")]
	public async Task<ActionResult<IReadOnlyList<SearchMovieResultDto>>> SearchMovies(
		[FromQuery] string? q,
		CancellationToken cancellationToken = default)
	{
		var term = (q ?? string.Empty).Trim();
		if (term.Length == 0)
		{
			return Ok(Array.Empty<SearchMovieResultDto>());
		}

		var radarrScope = await TryResolveDefaultRadarrScopeAsync(settingsRepo, cancellationToken).ConfigureAwait(false);
		if (radarrScope is not null)
		{
			var settings = await settingsRepo.GetAsync(radarrScope, cancellationToken).ConfigureAwait(false);
			if (settings is not null && !string.IsNullOrWhiteSpace(settings.RadarrBaseUrl) && !string.IsNullOrWhiteSpace(settings.RadarrApiKey))
			{
				var connection = new RadarrConnection(settings.RadarrBaseUrl, settings.RadarrApiKey);
				var lookup = await radarrClient.LookupByTermAsync(connection, term, cancellationToken).ConfigureAwait(false);
				var results = lookup
					.Select(m => new SearchMovieResultDto(
						m.TmdbId,
						m.Title,
						m.Year,
						GetPosterUrl(m),
						GetBackdropUrl(m)))
					.ToList();
				return Ok(results);
			}
		}

		// Local cache first, then TMDB API
		var settingsTmdb = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var opts = tmdbOptions.Value;
		var cacheMovies = await metadataStore.ListMoviesAsync(0, 50, missingDetailsOnly: false, titleQuery: term, cancellationToken).ConfigureAwait(false);
		var byId = new Dictionary<int, SearchMovieResultDto>();
		foreach (var m in cacheMovies)
		{
			byId[m.TmdbId] = new SearchMovieResultDto(
				m.TmdbId,
				m.Title ?? $"TMDB:{m.TmdbId}",
				m.ReleaseYear,
				BuildImageUrl(settingsTmdb, opts.ImageBaseUrl, m.PosterPath, opts.PosterSize),
				BuildImageUrl(settingsTmdb, opts.ImageBaseUrl, m.BackdropPath, opts.BackdropSize));
		}

		var apiResults = await tmdbClient.SearchMoviesAsync(term, page: 1, cancellationToken).ConfigureAwait(false);
		foreach (var m in apiResults)
		{
			if (byId.ContainsKey(m.Id))
				continue;
			byId[m.Id] = new SearchMovieResultDto(
				m.Id,
				m.Title ?? m.OriginalTitle ?? $"TMDB:{m.Id}",
				TryParseYear(m.ReleaseDate),
				BuildImageUrl(settingsTmdb, opts.ImageBaseUrl, m.PosterPath, opts.PosterSize),
				BuildImageUrl(settingsTmdb, opts.ImageBaseUrl, m.BackdropPath, opts.BackdropSize));
		}

		return Ok(byId.Values.ToList());
	}

	private static string? GetPosterUrl(RadarrLookupMovie m)
	{
		var poster = m.Images?.FirstOrDefault(i => string.Equals(i.CoverType, "poster", StringComparison.OrdinalIgnoreCase));
		return poster?.RemoteUrl ?? poster?.Url ?? m.Images?.FirstOrDefault()?.RemoteUrl ?? m.Images?.FirstOrDefault()?.Url;
	}

	private static string? GetBackdropUrl(RadarrLookupMovie m)
	{
		var backdrop = m.Images?.FirstOrDefault(i => string.Equals(i.CoverType, "backdrop", StringComparison.OrdinalIgnoreCase));
		return backdrop?.RemoteUrl ?? backdrop?.Url ?? m.Images?.LastOrDefault()?.RemoteUrl ?? m.Images?.LastOrDefault()?.Url;
	}

	private static string? BuildImageUrl(TmdbMetadataSettings settings, string imageBaseUrl, string? path, string size)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;
		var normalizedPath = path.Trim().TrimStart('/');
		var normalizedSize = (size ?? "original").Trim().Trim('/');
		if (settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0)
			return $"/api/v1/tmdb/images/{normalizedSize}/{normalizedPath}";
		var baseUrl = (imageBaseUrl ?? string.Empty).TrimEnd('/');
		return $"{baseUrl}/{normalizedSize}/{normalizedPath}";
	}

	private static int? TryParseYear(string? releaseDate)
	{
		if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
			return null;
		return int.TryParse(releaseDate.AsSpan(0, 4), System.Globalization.NumberStyles.None, null, out var year) ? year : null;
	}

	private static async Task<ServiceScope?> TryResolveDefaultRadarrScopeAsync(
		IServiceSettingsRepository repo,
		CancellationToken cancellationToken)
	{
		var rows = await repo.ListAsync(ServiceType.Radarr, cancellationToken).ConfigureAwait(false);
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
}
