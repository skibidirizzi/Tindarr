using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Contracts.Interactions;
using Tindarr.Contracts.Movies;
using Tindarr.Contracts.Tmdb;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tmdb")]
public sealed class TmdbController(
	ITmdbClient tmdbClient,
	IUserPreferencesService preferencesService,
	IInteractionStore interactionStore,
	IOptions<TmdbOptions> tmdbOptions,
	ITmdbCacheAdmin cacheAdmin,
	ITmdbMetadataStore metadataStore,
	ITmdbImageCache imageCache,
	ITmdbBuildJob buildJob) : ControllerBase
{
	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("cache/movies")]
	public async Task<ActionResult<TmdbStoredMovieAdminListResponse>> ListStoredMovies(
		[FromQuery] int skip = 0,
		[FromQuery] int take = 50,
		[FromQuery] bool missingDetailsOnly = false,
		[FromQuery] bool missingImagesOnly = false,
		[FromQuery] string? q = null,
		CancellationToken cancellationToken = default)
	{
		skip = Math.Max(0, skip);
		take = Math.Clamp(take, 1, 200);

		var settings = await metadataStore.GetSettingsAsync(cancellationToken);
		var canCheckImages = settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0;
		var posterSize = tmdbOptions.Value.PosterSize;
		var backdropSize = tmdbOptions.Value.BackdropSize;

		var items = new List<TmdbStoredMovieAdminDto>(capacity: take);
		var nextSkip = skip;
		var hasMore = false;

		// If missingImagesOnly is requested, we may need to scan forward because image cache
		// status isn't a SQL filter in the metadata store.
		for (var iter = 0; iter < 10 && items.Count < take; iter++)
		{
			var chunk = await metadataStore.ListMoviesAsync(nextSkip, take, missingDetailsOnly, q, cancellationToken);
			if (chunk.Count == 0)
			{
				hasMore = false;
				break;
			}

			nextSkip += chunk.Count;
			hasMore = chunk.Count == take;

			foreach (var m in chunk)
			{
				var posterCached = false;
				var backdropCached = false;
				if (canCheckImages)
				{
					if (!string.IsNullOrWhiteSpace(m.PosterPath))
					{
						posterCached = await imageCache.HasAsync(posterSize, m.PosterPath!, cancellationToken);
					}
					if (!string.IsNullOrWhiteSpace(m.BackdropPath))
					{
						backdropCached = await imageCache.HasAsync(backdropSize, m.BackdropPath!, cancellationToken);
					}
				}

				if (missingImagesOnly)
				{
					var posterMissing = !string.IsNullOrWhiteSpace(m.PosterPath) && !posterCached;
					var backdropMissing = !string.IsNullOrWhiteSpace(m.BackdropPath) && !backdropCached;
					if (!posterMissing && !backdropMissing)
					{
						continue;
					}
				}

				items.Add(new TmdbStoredMovieAdminDto(
					TmdbId: m.TmdbId,
					Title: m.Title,
					ReleaseYear: m.ReleaseYear,
					PosterPath: m.PosterPath,
					BackdropPath: m.BackdropPath,
					DetailsFetchedAtUtc: m.DetailsFetchedAtUtc,
					UpdatedAtUtc: m.UpdatedAtUtc,
					PosterCached: posterCached,
					BackdropCached: backdropCached));

				if (items.Count >= take)
				{
					break;
				}
			}
		}

		return Ok(new TmdbStoredMovieAdminListResponse(
			Items: items,
			Skip: skip,
			Take: take,
			NextSkip: nextSkip,
			HasMore: hasMore));
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("cache/movies/{tmdbId:int}/fill-details")]
	public async Task<ActionResult> FillMovieDetails(
		[FromRoute] int tmdbId,
		[FromQuery] bool rateLimitOverride = false,
		CancellationToken cancellationToken = default)
	{
		if (!tmdbOptions.Value.HasCredentials)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable,
				"TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken).");
		}

		var existing = await metadataStore.GetMovieAsync(tmdbId, cancellationToken);
		if (existing is null)
		{
			return NotFound("Movie not found in local TMDB metadata store.");
		}

		var previousBypass = TmdbRateLimitingHandler.BypassRateLimit.Value;
		TmdbRateLimitingHandler.BypassRateLimit.Value = rateLimitOverride;
		try
		{
			var details = await tmdbClient.GetMovieDetailsAsync(tmdbId, cancellationToken);
			if (details is null)
			{
				return NotFound("Movie not found on TMDB.");
			}

			await metadataStore.UpdateMovieDetailsAsync(details, cancellationToken);
			return NoContent();
		}
		finally
		{
			TmdbRateLimitingHandler.BypassRateLimit.Value = previousBypass;
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("cache/movies/{tmdbId:int}/fetch-images")]
	public async Task<ActionResult<TmdbFetchMovieImagesResultDto>> FetchMovieImages(
		[FromRoute] int tmdbId,
		[FromQuery] bool includePoster = true,
		[FromQuery] bool includeBackdrop = true,
		CancellationToken cancellationToken = default)
	{
		if (!tmdbOptions.Value.HasCredentials)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable,
				"TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken).");
		}

		var settings = await metadataStore.GetSettingsAsync(cancellationToken);
		if (settings.PosterMode != TmdbPosterMode.LocalProxy || settings.ImageCacheMaxMb <= 0)
		{
			return BadRequest("Poster mode is not set to LocalProxy or image cache is disabled.");
		}

		var movie = await metadataStore.GetMovieAsync(tmdbId, cancellationToken);
		if (movie is null)
		{
			return NotFound("Movie not found in local TMDB metadata store.");
		}

		var posterFetched = false;
		var backdropFetched = false;

		if (includePoster && !string.IsNullOrWhiteSpace(movie.PosterPath))
		{
			posterFetched = await imageCache.GetOrFetchAsync(tmdbOptions.Value.PosterSize, movie.PosterPath!, cancellationToken) is not null;
		}

		if (includeBackdrop && !string.IsNullOrWhiteSpace(movie.BackdropPath))
		{
			backdropFetched = await imageCache.GetOrFetchAsync(tmdbOptions.Value.BackdropSize, movie.BackdropPath!, cancellationToken) is not null;
		}

		// Apply pruning to keep within configured cap.
		var maxBytes = (long)Math.Max(0, settings.ImageCacheMaxMb) * 1024L * 1024L;
		await imageCache.PruneAsync(maxBytes, cancellationToken);

		var msg = (includePoster && string.IsNullOrWhiteSpace(movie.PosterPath)) || (includeBackdrop && string.IsNullOrWhiteSpace(movie.BackdropPath))
			? "Movie is missing poster/backdrop paths."
			: null;

		return Ok(new TmdbFetchMovieImagesResultDto(
			TmdbId: tmdbId,
			PosterFetched: posterFetched,
			BackdropFetched: backdropFetched,
			Message: msg));
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("cache/settings")]
	public async Task<ActionResult<TmdbCacheSettingsDto>> GetCacheSettings(CancellationToken cancellationToken = default)
	{
		var max = await cacheAdmin.GetMaxRowsAsync(cancellationToken);
		var count = await cacheAdmin.GetRowCountAsync(cancellationToken);
		var settings = await metadataStore.GetSettingsAsync(cancellationToken);
		var stats = await metadataStore.GetStatsAsync(cancellationToken);
		var imageBytes = await imageCache.GetTotalBytesAsync(cancellationToken);
		return Ok(new TmdbCacheSettingsDto(
			MaxRows: max,
			CurrentRows: count,
			MaxMovies: settings.MaxMovies,
			CurrentMovies: stats.MovieCount,
			ImageCacheMaxMb: settings.ImageCacheMaxMb,
			ImageCacheBytes: imageBytes,
			PosterMode: settings.PosterMode.ToString()));
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPut("cache/settings")]
	public async Task<ActionResult<TmdbCacheSettingsDto>> UpdateCacheSettings(
		[FromBody] UpdateTmdbCacheSettingsRequest request,
		CancellationToken cancellationToken = default)
	{
		if (request.MaxRows < 200)
		{
			return BadRequest("MaxRows must be at least 200.");
		}

		if (request.MaxMovies < 500)
		{
			return BadRequest("MaxMovies must be at least 500.");
		}

		if (request.ImageCacheMaxMb < 0)
		{
			return BadRequest("ImageCacheMaxMb must be at least 0.");
		}

		if (!Enum.TryParse<TmdbPosterMode>(request.PosterMode ?? string.Empty, ignoreCase: true, out var posterMode))
		{
			return BadRequest("PosterMode must be 'Tmdb' or 'LocalProxy'.");
		}

		await cacheAdmin.SetMaxRowsAsync(request.MaxRows, cancellationToken);

		var current = await metadataStore.GetSettingsAsync(cancellationToken);
		_ = await metadataStore.SetSettingsAsync(
			current with { MaxMovies = request.MaxMovies, ImageCacheMaxMb = request.ImageCacheMaxMb, PosterMode = posterMode },
			cancellationToken);

		// Apply pruning immediately when lowering the limit.
		await imageCache.PruneAsync((long)request.ImageCacheMaxMb * 1024L * 1024L, cancellationToken);

		return await GetCacheSettings(cancellationToken);
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("build/start")]
	public ActionResult<TmdbBuildStatusDto> StartBuild(
		[FromBody] StartTmdbBuildRequest request)
	{
		var started = buildJob.TryStart(request);
		if (!started)
		{
			return Conflict(buildJob.GetStatus());
		}

		return Ok(buildJob.GetStatus());
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("build/cancel")]
	public ActionResult<TmdbBuildStatusDto> CancelBuild([FromQuery] string? reason = null)
	{
		_ = buildJob.TryCancel(reason ?? "Canceled by admin");
		return Ok(buildJob.GetStatus());
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("build/status")]
	public ActionResult<TmdbBuildStatusDto> GetBuildStatus()
	{
		return Ok(buildJob.GetStatus());
	}

	[HttpGet("discover")]
	public async Task<ActionResult<SwipeDeckResponse>> Discover(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int limit = 10,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		limit = Math.Clamp(limit, 1, 50);

		if (!tmdbOptions.Value.HasCredentials)
		{
			// TMDB not configured yet on this machine.
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken).");
		}

		var userId = User.GetUserId();
		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken);

		// Pull a bigger pool than requested, then filter seen items.
		var candidatePoolSize = Math.Clamp(limit * 5, 10, 200);
		var candidates = await tmdbClient.DiscoverAsync(prefs, page: 1, candidatePoolSize, cancellationToken);

		var interacted = await interactionStore.GetInteractedTmdbIdsAsync(userId, scope!, cancellationToken);

		var filtered = candidates
			.Where(card => !interacted.Contains(card.TmdbId))
			.Take(limit)
			.ToList();

		return Ok(new SwipeDeckResponse(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			filtered.Select(Map).ToList()));
	}

	[HttpGet("movies/{tmdbId:int}")]
	public async Task<ActionResult<MovieDetailsDto>> GetMovieDetails(
		[FromRoute] int tmdbId,
		CancellationToken cancellationToken = default)
	{
		if (!tmdbOptions.Value.HasCredentials)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken).");
		}

		var details = await tmdbClient.GetMovieDetailsAsync(tmdbId, cancellationToken);
		if (details is null)
		{
			return NotFound();
		}

		return Ok(details);
	}

	private static SwipeCardDto Map(SwipeCard card)
	{
		return new SwipeCardDto(card.TmdbId, card.Title, card.Overview, card.PosterUrl, card.BackdropUrl, card.ReleaseYear, card.Rating);
	}
}

