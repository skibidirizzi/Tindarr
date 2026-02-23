using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Contracts.Emby;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/emby")]
public sealed class EmbyController : MediaServerControllerBase
{
	private readonly IEmbyService _embyService;
	private readonly ILibraryCacheRepository _libraryCache;

	public EmbyController(
		IEmbyService embyService,
		IServiceSettingsRepository settingsRepo,
		ILibraryCacheRepository libraryCache)
		: base(settingsRepo)
	{
		_embyService = embyService;
		_libraryCache = libraryCache;
	}

	[HttpGet("servers")]
	public async Task<ActionResult<IReadOnlyList<EmbyServerDto>>> ListServers(CancellationToken cancellationToken)
	{
		var servers = await _embyService.ListServersAsync(cancellationToken);
		var result = new List<EmbyServerDto>(servers.Count);
		foreach (var s in servers)
		{
			var scope = new ServiceScope(ServiceType.Emby, s.ServerId);
			var count = await _libraryCache.CountTmdbIdsAsync(scope, cancellationToken).ConfigureAwait(false);
			result.Add(new EmbyServerDto(
				s.ServerId,
				s.Name,
				s.BaseUrl,
				s.Version,
				s.LastLibrarySyncUtc,
				s.UpdatedAtUtc,
				count));
		}
		return Ok(result);
	}

	[HttpGet("settings")]
	public async Task<ActionResult<EmbySettingsDto>> GetSettings(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Emby, "emby", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var settings = await _embyService.GetSettingsAsync(scope!, cancellationToken);
		return Ok(MapSettings(scope!, settings));
	}

	[HttpPut("settings")]
	public async Task<ActionResult<EmbySettingsDto>> UpsertSettings(
		[FromQuery] bool confirmNewInstance,
		[FromBody] UpdateEmbySettingsRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var updated = await _embyService.UpsertSettingsAsync(
				new EmbySettingsUpsert(request.BaseUrl, request.ApiKey),
				confirmNewInstance,
				cancellationToken);

			var scope = new ServiceScope(ServiceType.Emby, updated.ServerId);
			return Ok(MapSettings(scope, updated));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Emby request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Emby request timed out.");
		}
	}

	[HttpPost("test-connection")]
	public async Task<ActionResult<EmbyConnectionTestResponse>> TestConnection(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Emby, "emby", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var result = await _embyService.TestConnectionAsync(scope!, cancellationToken);
		return Ok(new EmbyConnectionTestResponse(result.Ok, result.Message));
	}

	[HttpPost("library/sync")]
	public async Task<ActionResult<EmbyLibrarySyncResponse>> SyncLibrary(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, ServiceType.Emby, "emby", out var scope, out var errorResult))
		{
			return errorResult!;
		}

		try
		{
			var result = await _embyService.SyncLibraryAsync(scope!, cancellationToken);
			return Ok(new EmbyLibrarySyncResponse(
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
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Emby request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Emby request timed out.");
		}
	}

	private static EmbySettingsDto MapSettings(ServiceScope scope, Tindarr.Application.Abstractions.Persistence.ServiceSettingsRecord? settings)
	{
		var configured = settings is not null && !string.IsNullOrWhiteSpace(settings.EmbyBaseUrl);
		var hasApiKey = settings is not null && !string.IsNullOrWhiteSpace(settings.EmbyApiKey);

		return new EmbySettingsDto(
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			configured,
			settings?.EmbyBaseUrl,
			hasApiKey,
			settings?.EmbyServerName,
			settings?.EmbyServerVersion,
			settings?.EmbyLastLibrarySyncUtc,
			settings?.UpdatedAtUtc);
	}

	[HttpDelete("servers/{serverId}")]
	public async Task<IActionResult> DeleteServer([FromRoute] string serverId, CancellationToken cancellationToken)
	{
		return await DeleteServerAsync(ServiceType.Emby, serverId, cancellationToken).ConfigureAwait(false);
	}
}
