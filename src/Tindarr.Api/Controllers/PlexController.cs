using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Api.Services;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Contracts.Movies;
using Tindarr.Contracts.Plex;
using Tindarr.Contracts.Tmdb;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/plex")]
public sealed class PlexController(
	IPlexService plexService,
	IPlexLibrarySyncJobService librarySyncJob,
	IPlexLibraryCacheRepository plexLibraryCache,
	ITmdbMetadataStore tmdbMetadataStore,
	ITmdbImageCache imageCache,
	IOptions<TmdbOptions> tmdbOptions,
	IUserPreferencesService preferencesService,
	IServiceSettingsRepository settingsRepo) : MediaServerControllerBase(settingsRepo)
{
	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("library/sync/status/stream")]
	public async Task GetLibrarySyncStatusStream(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			Response.StatusCode = (errorResult as ObjectResult)?.StatusCode ?? StatusCodes.Status400BadRequest;
			return;
		}

		Response.ContentType = "text/event-stream";
		Response.Headers.CacheControl = "no-cache";
		Response.Headers.Connection = "keep-alive";
		Response.Headers["X-Accel-Buffering"] = "no";

		var jsonOptions = HttpContext.RequestServices
			.GetRequiredService<IOptions<JsonOptions>>()
			.Value
			.JsonSerializerOptions;

		PlexLibrarySyncStatusDto ToDto(PlexLibrarySyncJobStatus status) => new(
			ServiceType: status.ServiceType,
			ServerId: status.ServerId,
			State: status.State.ToString().ToLowerInvariant(),
			TotalSections: status.TotalSections,
			ProcessedSections: status.ProcessedSections,
			TotalItems: status.TotalItems,
			ProcessedItems: status.ProcessedItems,
			TmdbIdsFound: status.TmdbIdsFound,
			StartedAtUtc: status.StartedAtUtc,
			FinishedAtUtc: status.FinishedAtUtc,
			Message: status.Message,
			UpdatedAtUtc: status.UpdatedAtUtc);

		static async Task WriteComment(HttpResponse response, string text, CancellationToken ct)
		{
			await response.WriteAsync($": {text}\n\n", ct);
			await response.Body.FlushAsync(ct);
		}

		static async Task WriteEvent(HttpResponse response, string eventName, string json, CancellationToken ct)
		{
			await response.WriteAsync($"event: {eventName}\n", ct);
			await response.WriteAsync($"data: {json}\n\n", ct);
			await response.Body.FlushAsync(ct);
		}

		var channel = Channel.CreateUnbounded<PlexLibrarySyncJobStatus>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false
		});

		void Push(PlexLibrarySyncJobStatus status)
		{
			if (status.ServiceType.Equals(scope!.ServiceType.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
				&& status.ServerId.Equals(scope.ServerId, StringComparison.OrdinalIgnoreCase))
			{
				channel.Writer.TryWrite(status);
			}
		}

		EventHandler<PlexLibrarySyncJobStatus> handler = (_, status) => Push(status);
		librarySyncJob.StatusChanged += handler;

		var keepAliveInterval = TimeSpan.FromSeconds(15);

		try
		{
			var initial = librarySyncJob.GetStatus(scope!);
			await WriteEvent(Response, "status", JsonSerializer.Serialize(ToDto(initial), jsonOptions), cancellationToken);

			// If nothing is running, we can close the stream after emitting the snapshot.
			if (initial.State != PlexLibrarySyncJobState.Running)
			{
				return;
			}

			while (!cancellationToken.IsCancellationRequested)
			{
				var delayTask = Task.Delay(keepAliveInterval, cancellationToken);
				var readTask = channel.Reader.ReadAsync(cancellationToken).AsTask();
				var completed = await Task.WhenAny(delayTask, readTask).ConfigureAwait(false);

				if (completed == delayTask)
				{
					await WriteComment(Response, "keepalive", cancellationToken);
					continue;
				}

				var next = await readTask.ConfigureAwait(false);
				// Drain any queued statuses; only emit the latest.
				while (channel.Reader.TryRead(out var extra))
				{
					next = extra;
				}

				await WriteEvent(Response, "status", JsonSerializer.Serialize(ToDto(next), jsonOptions), cancellationToken);
				if (next.State != PlexLibrarySyncJobState.Running)
				{
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// client disconnected
		}
		finally
		{
			librarySyncJob.StatusChanged -= handler;
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("auth/status")]
	public async Task<ActionResult<PlexAuthStatusResponse>> GetAuthStatus(CancellationToken cancellationToken)
	{
		var status = await plexService.GetAuthStatusAsync(cancellationToken);
		return Ok(new PlexAuthStatusResponse(status.HasClientIdentifier, status.HasAuthToken));
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("pin")]
	public async Task<ActionResult<PlexPinCreateResponse>> CreatePin(CancellationToken cancellationToken)
	{
		try
		{
			var pin = await plexService.CreatePinAsync(cancellationToken);
			return Ok(new PlexPinCreateResponse(pin.PinId, pin.Code, pin.ExpiresAtUtc, pin.AuthUrl));
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Plex request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Plex request timed out.");
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("pins/{pinId:long}/verify")]
	public async Task<ActionResult<PlexPinStatusResponse>> VerifyPin(
		[FromRoute] long pinId,
		CancellationToken cancellationToken)
	{
		try
		{
			var status = await plexService.VerifyPinAsync(pinId, cancellationToken);
			return Ok(new PlexPinStatusResponse(status.PinId, status.Code, status.ExpiresAtUtc, status.Authorized));
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Plex request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Plex request timed out.");
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("servers")]
	public async Task<ActionResult<IReadOnlyList<PlexServerDto>>> ListServers(CancellationToken cancellationToken)
	{
		var servers = await plexService.ListServersAsync(cancellationToken);
		return Ok(servers.Select(MapServer).ToList());
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("servers/sync")]
	public async Task<ActionResult<IReadOnlyList<PlexServerDto>>> SyncServers(CancellationToken cancellationToken)
	{
		try
		{
			var servers = await plexService.RefreshServersAsync(cancellationToken);
			return Ok(servers.Select(MapServer).ToList());
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Plex request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Plex request timed out.");
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpDelete("servers/{serverId}")]
	public async Task<IActionResult> DeleteServer([FromRoute] string serverId, CancellationToken cancellationToken)
	{
		return await DeleteServerAsync(ServiceType.Plex, serverId, cancellationToken, validateDelete: id =>
			string.Equals(id, PlexConstants.AccountServerId, StringComparison.OrdinalIgnoreCase)
				? (false, "Cannot delete plex-account settings.")
				: (true, null)).ConfigureAwait(false);
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("library/sync")]
	public async Task<ActionResult<PlexLibrarySyncResponse>> SyncLibrary(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		try
		{
			var result = await plexService.SyncLibraryAsync(scope!, cancellationToken);
			return Ok(new PlexLibrarySyncResponse(
				scope!.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				result.Count,
				result.SyncedAtUtc));
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Plex request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Plex request timed out.");
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpPost("library/sync/async")]
	public async Task<ActionResult<PlexLibrarySyncStatusDto>> StartLibrarySync(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var status = await librarySyncJob.StartAsync(scope!, cancellationToken);
		return Accepted(new PlexLibrarySyncStatusDto(
			ServiceType: status.ServiceType,
			ServerId: status.ServerId,
			State: status.State.ToString().ToLowerInvariant(),
			TotalSections: status.TotalSections,
			ProcessedSections: status.ProcessedSections,
			TotalItems: status.TotalItems,
			ProcessedItems: status.ProcessedItems,
			TmdbIdsFound: status.TmdbIdsFound,
			StartedAtUtc: status.StartedAtUtc,
			FinishedAtUtc: status.FinishedAtUtc,
			Message: status.Message,
			UpdatedAtUtc: status.UpdatedAtUtc));
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("library/sync/status")]
	public ActionResult<PlexLibrarySyncStatusDto> GetLibrarySyncStatus(
		[FromQuery] string serviceType,
		[FromQuery] string serverId)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var status = librarySyncJob.GetStatus(scope!);
		return Ok(new PlexLibrarySyncStatusDto(
			ServiceType: status.ServiceType,
			ServerId: status.ServerId,
			State: status.State.ToString().ToLowerInvariant(),
			TotalSections: status.TotalSections,
			ProcessedSections: status.ProcessedSections,
			TotalItems: status.TotalItems,
			ProcessedItems: status.ProcessedItems,
			TmdbIdsFound: status.TmdbIdsFound,
			StartedAtUtc: status.StartedAtUtc,
			FinishedAtUtc: status.FinishedAtUtc,
			Message: status.Message,
			UpdatedAtUtc: status.UpdatedAtUtc));
	}

	[HttpGet("library")]
	public async Task<ActionResult<PlexLibraryResponse>> GetLibrary(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int limit = 50,
		CancellationToken cancellationToken = default)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		try
		{
			var userId = User.GetUserId();
			var preferences = await preferencesService.GetOrDefaultAsync(userId, cancellationToken);
			var items = await plexService.GetLibraryAsync(scope!, preferences, limit, cancellationToken);

			return Ok(new PlexLibraryResponse(
				scope!.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				items.Count,
				items));
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Plex request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Plex request timed out.");
		}
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("library/missing-details")]
	public async Task<ActionResult<PlexLibraryMissingDetailsResponse>> ListLibraryItemsMissingDetails(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int take = 100,
		CancellationToken cancellationToken = default)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		take = Math.Clamp(take, 1, 500);

		var cached = await plexLibraryCache.ListItemsAsync(scope!, skip: 0, take: 2_000, cancellationToken).ConfigureAwait(false);
		if (cached.Count == 0)
		{
			return Ok(new PlexLibraryMissingDetailsResponse(
				ServiceType: scope!.ServiceType.ToString().ToLowerInvariant(),
				ServerId: scope.ServerId,
				Count: 0,
				Items: Array.Empty<PlexLibraryMissingDetailsItemDto>()));
		}

		var missing = new List<PlexLibraryMissingDetailsItemDto>(capacity: Math.Min(take, cached.Count));
		foreach (var item in cached)
		{
			var stored = await tmdbMetadataStore.GetMovieAsync(item.TmdbId, cancellationToken).ConfigureAwait(false);
			var hasDetails = stored?.DetailsFetchedAtUtc is not null;
			if (hasDetails)
			{
				continue;
			}

			missing.Add(new PlexLibraryMissingDetailsItemDto(item.TmdbId, item.Title));
			if (missing.Count >= take)
			{
				break;
			}
		}

		return Ok(new PlexLibraryMissingDetailsResponse(
			ServiceType: scope!.ServiceType.ToString().ToLowerInvariant(),
			ServerId: scope.ServerId,
			Count: missing.Count,
			Items: missing));
	}

	[Authorize(Policy = Policies.AdminOnly)]
	[HttpGet("library/cache/movies")]
	public async Task<ActionResult<TmdbStoredMovieAdminListResponse>> ListLibraryCacheMovies(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int skip = 0,
		[FromQuery] int take = 50,
		[FromQuery] bool missingDetailsOnly = false,
		[FromQuery] bool missingImagesOnly = false,
		CancellationToken cancellationToken = default)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Plex, "plex", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		skip = Math.Max(0, skip);
		take = Math.Clamp(take, 1, 200);

		var settings = await tmdbMetadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var canCheckImages = settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0;
		var posterSize = tmdbOptions.Value.PosterSize;
		var backdropSize = tmdbOptions.Value.BackdropSize;

		var items = new List<TmdbStoredMovieAdminDto>(capacity: take);
		var nextSkip = skip;
		var hasMore = false;

		// Like TMDB cache listing: if missingImagesOnly is requested, scan forward to fill the page.
		for (var iter = 0; iter < 10 && items.Count < take; iter++)
		{
			var cacheChunk = await plexLibraryCache.ListItemsAsync(scope!, nextSkip, take, cancellationToken).ConfigureAwait(false);
			if (cacheChunk.Count == 0)
			{
				hasMore = false;
				break;
			}

			nextSkip += cacheChunk.Count;
			hasMore = cacheChunk.Count == take;

			foreach (var entry in cacheChunk)
			{
				var stored = await tmdbMetadataStore.GetMovieAsync(entry.TmdbId, cancellationToken).ConfigureAwait(false);
				var title = stored?.Title ?? entry.Title;
				var hasDetails = stored?.DetailsFetchedAtUtc is not null;
				if (missingDetailsOnly && hasDetails)
				{
					continue;
				}

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

				if (missingImagesOnly)
				{
					var posterMissing = !string.IsNullOrWhiteSpace(stored?.PosterPath) && !posterCached;
					var backdropMissing = !string.IsNullOrWhiteSpace(stored?.BackdropPath) && !backdropCached;
					if (!posterMissing && !backdropMissing)
					{
						continue;
					}
				}

				items.Add(new TmdbStoredMovieAdminDto(
					TmdbId: entry.TmdbId,
					Title: title,
					ReleaseYear: stored?.ReleaseYear,
					PosterPath: stored?.PosterPath,
					BackdropPath: stored?.BackdropPath,
					DetailsFetchedAtUtc: stored?.DetailsFetchedAtUtc,
					UpdatedAtUtc: stored?.UpdatedAtUtc,
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

	private static PlexServerDto MapServer(PlexServerRecord record)
	{
		return new PlexServerDto(
			record.ServerId,
			record.Name,
			record.BaseUrl,
			record.Version,
			record.Platform,
			record.Owned,
			record.Online,
			record.LastLibrarySyncUtc,
			record.UpdatedAtUtc);
	}
}
