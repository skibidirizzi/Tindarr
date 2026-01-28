using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Contracts.Radarr;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/radarr")]
public sealed class RadarrController(IRadarrService radarrService) : ControllerBase
{
	[HttpGet("settings")]
	public async Task<ActionResult<RadarrSettingsDto>> GetSettings(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var settings = await radarrService.GetSettingsAsync(scope!, cancellationToken);
		return Ok(MapSettings(scope!, settings));
	}

	[HttpPut("settings")]
	public async Task<ActionResult<RadarrSettingsDto>> UpsertSettings(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromBody] UpdateRadarrSettingsRequest request,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
		{
			return errorResult!;
		}

		try
		{
			var updated = await radarrService.UpsertSettingsAsync(
				scope!,
				new RadarrSettingsUpsert(
					request.BaseUrl,
					request.ApiKey,
					request.QualityProfileId,
					request.RootFolderPath,
					request.TagLabel,
					request.AutoAddEnabled),
				cancellationToken);

			return Ok(MapSettings(scope!, updated));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
	}

	[HttpPost("test-connection")]
	public async Task<ActionResult<RadarrConnectionTestResponse>> TestConnection(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!TryGetScope(serviceType, serverId, out var scope, out var errorResult))
		{
			return errorResult!;
		}

		var result = await radarrService.TestConnectionAsync(scope!, cancellationToken);
		return Ok(new RadarrConnectionTestResponse(result.Ok, result.Message));
	}

	[HttpGet("quality-profiles")]
	public async Task<ActionResult<IReadOnlyList<RadarrQualityProfileDto>>> GetQualityProfiles(
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
			var profiles = await radarrService.GetQualityProfilesAsync(scope!, cancellationToken);
			return Ok(profiles.Select(p => new RadarrQualityProfileDto(p.Id, p.Name)).ToList());
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Radarr request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Radarr request timed out.");
		}
	}

	[HttpGet("root-folders")]
	public async Task<ActionResult<IReadOnlyList<RadarrRootFolderDto>>> GetRootFolders(
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
			var folders = await radarrService.GetRootFoldersAsync(scope!, cancellationToken);
			return Ok(folders.Select(f => new RadarrRootFolderDto(f.Id, f.Path, f.FreeSpaceBytes)).ToList());
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Radarr request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Radarr request timed out.");
		}
	}

	[HttpPost("library/sync")]
	public async Task<ActionResult<RadarrLibrarySyncResponse>> SyncLibrary(
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
			var result = await radarrService.SyncLibraryAsync(scope!, cancellationToken);
			return Ok(new RadarrLibrarySyncResponse(
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
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "Radarr request failed.");
		}
		catch (TaskCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Radarr request timed out.");
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

		if (scope!.ServiceType != ServiceType.Radarr)
		{
			errorResult = new BadRequestObjectResult("ServiceType must be radarr.");
			return false;
		}

		return true;
	}

	private static RadarrSettingsDto MapSettings(ServiceScope scope, ServiceSettingsRecord? settings)
	{
		var configured = settings is not null && !string.IsNullOrWhiteSpace(settings.RadarrBaseUrl);
		var hasApiKey = settings is not null && !string.IsNullOrWhiteSpace(settings.RadarrApiKey);

		return new RadarrSettingsDto(
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			configured,
			settings?.RadarrBaseUrl,
			settings?.RadarrQualityProfileId,
			settings?.RadarrRootFolderPath,
			settings?.RadarrTagLabel,
			settings?.RadarrAutoAddEnabled ?? false,
			hasApiKey,
			settings?.RadarrLastLibrarySyncUtc,
			settings?.UpdatedAtUtc);
	}
}
