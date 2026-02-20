using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

/// <summary>
/// Shared base for Plex, Jellyfin, and Emby media server controllers.
/// Provides common scope validation and server deletion behavior.
/// </summary>
public abstract class MediaServerControllerBase(IServiceSettingsRepository settingsRepo) : ControllerBase
{
	protected IServiceSettingsRepository SettingsRepo { get; } = settingsRepo;

	/// <summary>
	/// Validates serviceType and serverId and ensures they match the expected media server type.
	/// </summary>
	protected static bool TryGetScope(
		string? serviceType,
		string? serverId,
		ServiceType expectedType,
		string expectedName,
		out ServiceScope? scope,
		out ActionResult? errorResult)
	{
		scope = null;
		errorResult = null;

		if (string.IsNullOrWhiteSpace(serviceType))
		{
			errorResult = new BadRequestObjectResult("ServiceType is required.");
			return false;
		}

		if (string.IsNullOrWhiteSpace(serverId))
		{
			errorResult = new BadRequestObjectResult("ServerId is required.");
			return false;
		}

		if (!ServiceTypeParser.TryParse(serviceType, out var parsedType))
		{
			errorResult = new BadRequestObjectResult("ServiceType is invalid.");
			return false;
		}

		scope = new ServiceScope(parsedType, serverId.Trim());

		if (scope.ServiceType != expectedType)
		{
			errorResult = new BadRequestObjectResult($"ServiceType must be {expectedName}.");
			return false;
		}

		return true;
	}

	/// <summary>
	/// Deletes server settings for the given scope. Optionally validate that the serverId is allowed to be deleted.
	/// </summary>
	protected async Task<IActionResult> DeleteServerAsync(
		ServiceType serviceType,
		string serverId,
		CancellationToken cancellationToken,
		Func<string, (bool allowed, string? errorMessage)>? validateDelete = null)
	{
		if (string.IsNullOrWhiteSpace(serverId))
		{
			return BadRequest("ServerId is required.");
		}

		var trimmed = serverId.Trim();
		if (validateDelete is not null)
		{
			var (allowed, errorMessage) = validateDelete(trimmed);
			if (!allowed)
			{
				return BadRequest(errorMessage ?? "Cannot delete this server.");
			}
		}

		var deleted = await SettingsRepo.DeleteAsync(new ServiceScope(serviceType, trimmed), cancellationToken).ConfigureAwait(false);
		return deleted ? NoContent() : NotFound("Server not found.");
	}
}
