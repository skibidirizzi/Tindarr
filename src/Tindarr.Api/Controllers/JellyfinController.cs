using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Contracts.Jellyfin;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/jellyfin")]
public sealed class JellyfinController(IJellyfinService jellyfinService) : ControllerBase
{
	[HttpGet("servers")]
	public async Task<ActionResult<IReadOnlyList<JellyfinServerDto>>> ListServers(CancellationToken cancellationToken)
	{
		var servers = await jellyfinService.ListServersAsync(cancellationToken);
		return Ok(servers.Select(s => new JellyfinServerDto(
			s.ServerId,
			s.Name,
			s.BaseUrl,
			s.Version,
			s.LastLibrarySyncUtc,
			s.UpdatedAtUtc)).ToList());
	}

	[HttpGet("settings")]
	public async Task<ActionResult<JellyfinSettingsDto>> GetSettings(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var settings = await jellyfinService.GetSettingsAsync(scope!, cancellationToken);
		return Ok(MapSettings(scope!, settings));
	}

	[HttpPut("settings")]
	public async Task<ActionResult<JellyfinSettingsDto>> UpsertSettings(
		[FromQuery] bool confirmNewInstance,
		[FromBody] UpdateJellyfinSettingsRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var updated = await jellyfinService.UpsertSettingsAsync(
				new JellyfinSettingsUpsert(request.BaseUrl, request.ApiKey),
				confirmNewInstance,
				cancellationToken);

			var scope = new ServiceScope(ServiceType.Jellyfin, updated.ServerId);
			return Ok(MapSettings(scope, updated));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Jellyfin request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Jellyfin request timed out.");
		}
	}

	[HttpPost("test-connection")]
	public async Task<ActionResult<JellyfinConnectionTestResponse>> TestConnection(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var result = await jellyfinService.TestConnectionAsync(scope!, cancellationToken);
		return Ok(new JellyfinConnectionTestResponse(result.Ok, result.Message));
	}

	[HttpPost("library/sync")]
	public async Task<ActionResult<JellyfinLibrarySyncResponse>> SyncLibrary(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
		{
			return errorResult!;
		}

		try
		{
			var result = await jellyfinService.SyncLibraryAsync(scope!, cancellationToken);
			return Ok(new JellyfinLibrarySyncResponse(
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
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Jellyfin request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Jellyfin request timed out.");
		}
	}

	private static bool TryGetScope(
		string serviceType,
		string serverId,
		out ServiceScope? scope,
		out ActionResult? errorResult)
	{
		errorResult = null;
		if (!ServiceScope.TryCreate(serviceType, serverId, out scope))
		{
			errorResult = new BadRequestObjectResult("ServiceType and ServerId are required.");
			return false;
		}

		if (scope!.ServiceType != ServiceType.Jellyfin)
		{
			errorResult = new BadRequestObjectResult("ServiceType must be jellyfin.");
			return false;
		}

		return true;
	}

	private static JellyfinSettingsDto MapSettings(ServiceScope scope, Tindarr.Application.Abstractions.Persistence.ServiceSettingsRecord? settings)
	{
		var configured = settings is not null && !string.IsNullOrWhiteSpace(settings.JellyfinBaseUrl);
		var hasApiKey = settings is not null && !string.IsNullOrWhiteSpace(settings.JellyfinApiKey);

		return new JellyfinSettingsDto(
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			configured,
			settings?.JellyfinBaseUrl,
			hasApiKey,
			settings?.JellyfinServerName,
			settings?.JellyfinServerVersion,
			settings?.JellyfinLastLibrarySyncUtc,
			settings?.UpdatedAtUtc);
	}
}
