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
public sealed class EmbyController(IEmbyService embyService, IServiceSettingsRepository settingsRepo)
	: MediaServerControllerBase(settingsRepo)
{
	[HttpGet("servers")]
	public async Task<ActionResult<IReadOnlyList<EmbyServerDto>>> ListServers(CancellationToken cancellationToken)
	{
		var servers = await embyService.ListServersAsync(cancellationToken);
		return Ok(servers.Select(s => new EmbyServerDto(
			s.ServerId,
			s.Name,
			s.BaseUrl,
			s.Version,
			s.LastLibrarySyncUtc,
			s.UpdatedAtUtc)).ToList());
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

		var settings = await embyService.GetSettingsAsync(scope!, cancellationToken);
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
			var updated = await embyService.UpsertSettingsAsync(
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

		var result = await embyService.TestConnectionAsync(scope!, cancellationToken);
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
			var result = await embyService.SyncLibraryAsync(scope!, cancellationToken);
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
