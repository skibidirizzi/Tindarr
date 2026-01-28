using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Contracts.Plex;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/plex")]
public sealed class PlexController(
	IPlexService plexService,
	IUserPreferencesService preferencesService) : ControllerBase
{
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
	[HttpPost("library/sync")]
	public async Task<ActionResult<PlexLibrarySyncResponse>> SyncLibrary(
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

	[HttpGet("library")]
	public async Task<ActionResult<PlexLibraryResponse>> GetLibrary(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int limit = 50,
		CancellationToken cancellationToken = default)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
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

		if (scope!.ServiceType != ServiceType.Plex)
		{
			errorResult = new BadRequestObjectResult("ServiceType must be plex.");
			return false;
		}

		return true;
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
