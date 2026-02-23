using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;
using Tindarr.Contracts.Admin;
using Tindarr.Contracts.Tmdb;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/admin/db")]
public sealed class AdminDbController(
	ITmdbMetadataStore tmdbMetadataStore,
	ITmdbImageCache imageCache,
	IOptions<TmdbOptions> tmdbOptions,
	IPlexLibraryCacheRepository plexLibraryCache,
	ILibraryCacheRepository libraryCache,
	IPopulateProgressReport populateProgressReport) : ControllerBase
{
	[HttpGet("populate-status")]
	public ActionResult<PopulateStatusDto> GetPopulateStatus()
	{
		return Ok(populateProgressReport.GetStatus());
	}

	[HttpGet("movies")]
	public async Task<ActionResult<AdminDbMovieListResponse>> ListMovies(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int skip = 0,
		[FromQuery] int take = 50,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		skip = Math.Max(0, skip);
		take = Math.Clamp(take, 1, 200);

		var settings = await tmdbMetadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var canCheckImages = settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0;
		var posterSize = tmdbOptions.Value.PosterSize;
		var backdropSize = tmdbOptions.Value.BackdropSize;

		if (scope!.ServiceType == ServiceType.Tmdb)
		{
			var stats = await tmdbMetadataStore.GetStatsAsync(cancellationToken).ConfigureAwait(false);
			var chunk = await tmdbMetadataStore.ListMoviesAsync(skip, take, missingDetailsOnly: false, titleQuery: null, cancellationToken).ConfigureAwait(false);
			var items = new List<TmdbStoredMovieAdminDto>(capacity: chunk.Count);
			foreach (var m in chunk)
			{
				var posterCached = false;
				var backdropCached = false;
				if (canCheckImages)
				{
					if (!string.IsNullOrWhiteSpace(m.PosterPath))
					{
						posterCached = await imageCache.HasAsync(posterSize, m.PosterPath!, cancellationToken).ConfigureAwait(false);
					}
					if (!string.IsNullOrWhiteSpace(m.BackdropPath))
					{
						backdropCached = await imageCache.HasAsync(backdropSize, m.BackdropPath!, cancellationToken).ConfigureAwait(false);
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
			}

			return Ok(new AdminDbMovieListResponse(
				Items: items,
				Skip: skip,
				Take: take,
				NextSkip: skip + chunk.Count,
				HasMore: chunk.Count == take,
				TotalCount: Math.Max(0, stats.MovieCount)));
		}

		var requested = take + 1;
		var itemsOut = new List<TmdbStoredMovieAdminDto>(capacity: take);
		var nextSkip = skip;
		var hasMore = false;
		var totalCount = 0;

		if (scope.ServiceType == ServiceType.Plex)
		{
			totalCount = await plexLibraryCache.CountTmdbIdsAsync(scope, cancellationToken).ConfigureAwait(false);
			var cacheChunk = await plexLibraryCache.ListItemsAsync(scope, skip, requested, cancellationToken).ConfigureAwait(false);
			hasMore = cacheChunk.Count > take;
			var page = hasMore ? cacheChunk.Take(take) : cacheChunk;
			nextSkip = skip + page.Count();

			foreach (var entry in page)
			{
				var stored = await tmdbMetadataStore.GetMovieAsync(entry.TmdbId, cancellationToken).ConfigureAwait(false);
				var title = stored?.Title ?? entry.Title;

				var posterCached = false;
				var backdropCached = false;
				if (canCheckImages)
				{
					if (!string.IsNullOrWhiteSpace(stored?.PosterPath))
					{
						posterCached = await imageCache.HasAsync(posterSize, stored!.PosterPath!, cancellationToken).ConfigureAwait(false);
					}
					if (!string.IsNullOrWhiteSpace(stored?.BackdropPath))
					{
						backdropCached = await imageCache.HasAsync(backdropSize, stored!.BackdropPath!, cancellationToken).ConfigureAwait(false);
					}
				}

				itemsOut.Add(new TmdbStoredMovieAdminDto(
					TmdbId: entry.TmdbId,
					Title: title,
					ReleaseYear: stored?.ReleaseYear,
					PosterPath: stored?.PosterPath,
					BackdropPath: stored?.BackdropPath,
					DetailsFetchedAtUtc: stored?.DetailsFetchedAtUtc,
					UpdatedAtUtc: stored?.UpdatedAtUtc,
					PosterCached: posterCached,
					BackdropCached: backdropCached));
			}
		}
		else
		{
			totalCount = await libraryCache.CountTmdbIdsAsync(scope, cancellationToken).ConfigureAwait(false);
			var idsChunk = await libraryCache.ListTmdbIdsAsync(scope, skip, requested, cancellationToken).ConfigureAwait(false);
			hasMore = idsChunk.Count > take;
			var page = hasMore ? idsChunk.Take(take) : idsChunk;
			nextSkip = skip + page.Count();

			foreach (var tmdbId in page)
			{
				var stored = await tmdbMetadataStore.GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
				var title = stored?.Title ?? $"TMDB #{tmdbId}";

				var posterCached = false;
				var backdropCached = false;
				if (canCheckImages)
				{
					if (!string.IsNullOrWhiteSpace(stored?.PosterPath))
					{
						posterCached = await imageCache.HasAsync(posterSize, stored!.PosterPath!, cancellationToken).ConfigureAwait(false);
					}
					if (!string.IsNullOrWhiteSpace(stored?.BackdropPath))
					{
						backdropCached = await imageCache.HasAsync(backdropSize, stored!.BackdropPath!, cancellationToken).ConfigureAwait(false);
					}
				}

				itemsOut.Add(new TmdbStoredMovieAdminDto(
					TmdbId: tmdbId,
					Title: title,
					ReleaseYear: stored?.ReleaseYear,
					PosterPath: stored?.PosterPath,
					BackdropPath: stored?.BackdropPath,
					DetailsFetchedAtUtc: stored?.DetailsFetchedAtUtc,
					UpdatedAtUtc: stored?.UpdatedAtUtc,
					PosterCached: posterCached,
					BackdropCached: backdropCached));
			}
		}

		return Ok(new AdminDbMovieListResponse(
			Items: itemsOut,
			Skip: skip,
			Take: take,
			NextSkip: nextSkip,
			HasMore: hasMore,
			TotalCount: Math.Max(0, totalCount)));
	}
}
