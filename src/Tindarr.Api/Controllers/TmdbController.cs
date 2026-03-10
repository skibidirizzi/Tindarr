using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Api.Services;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Ops;
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
[Route("api/v1/tmdb")]
public sealed class TmdbController(
	ITmdbClient tmdbClient,
	ISwipeDeckService swipeDeckService,
	IOptions<TmdbOptions> tmdbOptions,
	IEffectiveAdvancedSettings effectiveAdvancedSettings,
	ITmdbCacheAdmin cacheAdmin,
	ITmdbMetadataStore metadataStore,
	ITmdbImageCache imageCache,
	ITmdbBuildJob buildJob,
	Tindarr.Api.Services.TmdbBackupRestoreService backupRestoreService) : ControllerBase
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
		if (!effectiveAdvancedSettings.HasEffectiveTmdbCredentials())
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
		if (!effectiveAdvancedSettings.HasEffectiveTmdbCredentials())
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
			PosterMode: settings.PosterMode.ToString(),
			PrewarmOriginalLanguage: settings.PrewarmOriginalLanguage,
			PrewarmRegion: settings.PrewarmRegion));
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

		var prewarmLang = string.IsNullOrWhiteSpace(request.PrewarmOriginalLanguage) ? null : request.PrewarmOriginalLanguage.Trim();
		if (!string.IsNullOrWhiteSpace(prewarmLang))
		{
			if (prewarmLang.Length is < 2 or > 10)
			{
				return BadRequest("PrewarmOriginalLanguage must be blank or 2-10 characters.");
			}
			if (prewarmLang.Any(c => !(char.IsLetter(c) || c == '-' || c == '_')))
			{
				return BadRequest("PrewarmOriginalLanguage must contain only letters or '-'/'_'.");
			}
		}

		var prewarmRegion = string.IsNullOrWhiteSpace(request.PrewarmRegion) ? null : request.PrewarmRegion.Trim();
		if (!string.IsNullOrWhiteSpace(prewarmRegion))
		{
			if (prewarmRegion.Length is < 2 or > 10)
			{
				return BadRequest("PrewarmRegion must be blank or 2-10 characters.");
			}
			if (prewarmRegion.Any(c => !(char.IsLetter(c) || c == '-' || c == '_')))
			{
				return BadRequest("PrewarmRegion must contain only letters or '-'/'_'.");
			}
		}

		await cacheAdmin.SetMaxRowsAsync(request.MaxRows, cancellationToken);

		var current = await metadataStore.GetSettingsAsync(cancellationToken);
		_ = await metadataStore.SetSettingsAsync(
			current with
			{
				MaxMovies = request.MaxMovies,
				ImageCacheMaxMb = request.ImageCacheMaxMb,
				PosterMode = posterMode,
				PrewarmOriginalLanguage = prewarmLang,
				PrewarmRegion = prewarmRegion
			},
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

	[Authorize]
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

		// When TMDB is not configured, discover still works via Radarr (populates tmdb-metadata and serves cards from store).
		var userId = User.GetUserId();
		var cards = await swipeDeckService.GetDeckAsync(userId, scope!, limit, cancellationToken);

		return Ok(new SwipeDeckResponse(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope!.ServerId,
			cards.Select(Map).ToList()));
	}

	/// <summary>Movie details for display (e.g. room matches). Allow guests so they can see matched movie title, poster, etc.</summary>
	[Authorize(Policy = Policies.AllowGuests)]
	[HttpGet("movies/{tmdbId:int}")]
	public async Task<ActionResult<MovieDetailsDto>> GetMovieDetails(
		[FromRoute] int tmdbId,
		CancellationToken cancellationToken = default)
	{
		// Prefer live TMDB details when possible.
		var details = await tmdbClient.GetMovieDetailsAsync(tmdbId, cancellationToken);
		if (details is not null)
		{
			return Ok(details);
		}

		// Fallback: if TMDB credentials are missing (or TMDB is unavailable), serve cached details
		// from the local metadata store so UI can still render posters/backdrops.
		var stored = await metadataStore.GetMovieAsync(tmdbId, cancellationToken);
		if (stored is not null)
		{
			var settings = await metadataStore.GetSettingsAsync(cancellationToken);
			return Ok(new MovieDetailsDto(
				TmdbId: stored.TmdbId,
				Title: stored.Title,
				Overview: stored.Overview,
				PosterUrl: BuildImageUrl(settings, tmdbOptions.Value.ImageBaseUrl, stored.PosterPath, tmdbOptions.Value.PosterSize),
				BackdropUrl: BuildImageUrl(settings, tmdbOptions.Value.ImageBaseUrl, stored.BackdropPath, tmdbOptions.Value.BackdropSize),
				ReleaseDate: stored.ReleaseDate,
				ReleaseYear: stored.ReleaseYear,
				MpaaRating: stored.MpaaRating,
				Rating: stored.Rating,
				VoteCount: stored.VoteCount,
				Genres: stored.Genres,
				Regions: stored.Regions,
				OriginalLanguage: stored.OriginalLanguage,
				RuntimeMinutes: stored.RuntimeMinutes));
		}

		if (!effectiveAdvancedSettings.HasEffectiveTmdbCredentials())
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable,
				"TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken), and no cached movie details were found.");
		}

		return NotFound();
	}

	private static string? BuildImageUrl(TmdbMetadataSettings settings, string imageBaseUrl, string? path, string size)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var normalizedPath = path.Trim().TrimStart('/');
		var normalizedSize = (size ?? "original").Trim().Trim('/');
		if (settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0)
		{
			return $"/api/v1/tmdb/images/{normalizedSize}/{normalizedPath}";
		}
		var baseUrl = (imageBaseUrl ?? string.Empty).TrimEnd('/');
		return $"{baseUrl}/{normalizedSize}/{normalizedPath}";
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("backup/download")]
	public async Task<IActionResult> DownloadBackup(CancellationToken cancellationToken = default)
	{
		var includeImages = await backupRestoreService.GetIncludeImagesAsync(cancellationToken).ConfigureAwait(false);
		var stream = new MemoryStream();
		await backupRestoreService.WriteBackupZipAsync(stream, includeImages, cancellationToken).ConfigureAwait(false);
		stream.Position = 0;
		return File(stream, "application/zip", "tindarr-tmdb-backup.zip");
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("restore")]
	[RequestSizeLimit(500 * 1024 * 1024)]
	public async Task<ActionResult<TmdbRestoreResultDto>> Restore(IFormFile? file, CancellationToken cancellationToken = default)
	{
		if (file is null || file.Length == 0)
		{
			return BadRequest("No file uploaded.");
		}

		var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

		if (ext == ".zip")
		{
			await using var zipStream = file.OpenReadStream();
			var result = await backupRestoreService.RestoreFromZipAsync(zipStream, cancellationToken).ConfigureAwait(false);
			return Ok(result);
		}

		if (ext is ".sqlite" or ".sqlite3" or ".db")
		{
			string tempPath;
			try
			{
				tempPath = Path.Combine(Path.GetTempPath(), $"tindarr-tmdb-restore-{Guid.NewGuid():N}{ext}");
				await using (var stream = file.OpenReadStream())
				using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (IOException ex)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
			}

			try
			{
				var result = await metadataStore.ImportFromFileAsync(tempPath, cancellationToken).ConfigureAwait(false);
				return Ok(new TmdbRestoreResultDto(result.Inserted, result.Updated, result.Skipped, 0, result.NotImportedReasons));
			}
			finally
			{
				try { System.IO.File.Delete(tempPath); } catch { /* best-effort */ }
			}
		}

		return BadRequest("File must be a ZIP backup (.zip) or SQLite database (.sqlite, .sqlite3, or .db).");
	}

	private static SwipeCardDto Map(SwipeCard card)
	{
		return new SwipeCardDto(card.TmdbId, card.Title, card.Overview, card.PosterUrl, card.BackdropUrl, card.ReleaseYear, card.Rating);
	}
}

