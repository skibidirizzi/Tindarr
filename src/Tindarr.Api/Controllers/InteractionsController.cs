using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Contracts.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/interactions")]
public sealed class InteractionsController(
	IInteractionService interactionService,
	IRadarrPendingAddRepository radarrPendingAdds,
	IServiceSettingsRepository settingsRepo) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<InteractionListResponse>> List(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] SwipeActionDto? action,
		[FromQuery] int? tmdbId,
		[FromQuery] int limit = 200,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		// Hard cap to avoid unbounded reads.
		limit = Math.Clamp(limit, 1, 500);

		var userId = User.GetUserId();
		var items = await interactionService.ListAsync(
			userId,
			scope!,
			action is null ? null : MapAction(action.Value),
			tmdbId,
			limit,
			cancellationToken);

		return Ok(new InteractionListResponse(
			scope!.ServiceType.ToString(),
			scope.ServerId,
			items.Select(x => new InteractionDto(x.TmdbId, MapAction(x.Action), x.CreatedAtUtc)).ToList()));
	}

	[HttpPost]
	public async Task<ActionResult<SwipeResponse>> Create([FromBody] SwipeRequest request, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		if (scope!.ServiceType == ServiceType.Radarr)
		{
			return BadRequest("Radarr is not a swipe scope. Swipe TMDB discover instead.");
		}

		var isSuperlikePrivileged = User.IsInRole(Policies.AdminRole) || User.IsInRole(Policies.CuratorRole);

		if (request.Action == SwipeActionDto.Superlike && !isSuperlikePrivileged)
		{
			return Forbid();
		}

		var action = MapAction(request.Action);
		var userId = User.GetUserId();
		var interaction = await interactionService.AddAsync(userId, scope!, request.TmdbId, action, cancellationToken);

		if (scope!.ServiceType == ServiceType.Tmdb && action == InteractionAction.Superlike && isSuperlikePrivileged)
		{
			var radarrScope = await TryResolveDefaultRadarrScopeAsync(settingsRepo, cancellationToken).ConfigureAwait(false);
			if (radarrScope is not null)
			{
				// Small delay allows for quick undo without spamming Radarr.
				var readyAt = DateTimeOffset.UtcNow.AddSeconds(15);
				await radarrPendingAdds.TryEnqueueAsync(radarrScope, userId, request.TmdbId, readyAt, cancellationToken);
			}
		}

		return Ok(new SwipeResponse(interaction.TmdbId, MapAction(interaction.Action), interaction.CreatedAtUtc));
	}

	[HttpPost("undo")]
	public async Task<ActionResult<UndoResponse>> Undo([FromQuery] string serviceType, [FromQuery] string serverId, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		if (scope!.ServiceType == ServiceType.Radarr)
		{
			return BadRequest("Radarr is not a swipe scope.");
		}

		var userId = User.GetUserId();
		var interaction = await interactionService.UndoLastAsync(userId, scope!, cancellationToken);

		if (interaction is null)
		{
			return Ok(new UndoResponse(false, null, null, null));
		}

		if (scope!.ServiceType == ServiceType.Tmdb && interaction.Action == InteractionAction.Superlike)
		{
			var radarrScope = await TryResolveDefaultRadarrScopeAsync(settingsRepo, cancellationToken).ConfigureAwait(false);
			if (radarrScope is not null)
			{
				await radarrPendingAdds.TryCancelAsync(radarrScope, userId, interaction.TmdbId, cancellationToken);
			}
		}

		return Ok(new UndoResponse(true, interaction.TmdbId, MapAction(interaction.Action), interaction.CreatedAtUtc));
	}

	[Authorize]
	[HttpDelete]
	public async Task<IActionResult> ClearHistory(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var userId = User.GetUserId();
		await interactionService.ClearHistoryAsync(userId, scope!, cancellationToken);
		return NoContent();
	}

	private static InteractionAction MapAction(SwipeActionDto action)
	{
		return action switch
		{
			SwipeActionDto.Like => InteractionAction.Like,
			SwipeActionDto.Nope => InteractionAction.Nope,
			SwipeActionDto.Skip => InteractionAction.Skip,
			SwipeActionDto.Superlike => InteractionAction.Superlike,
			_ => InteractionAction.Skip
		};
	}

	private static SwipeActionDto MapAction(InteractionAction action)
	{
		return action switch
		{
			InteractionAction.Like => SwipeActionDto.Like,
			InteractionAction.Nope => SwipeActionDto.Nope,
			InteractionAction.Skip => SwipeActionDto.Skip,
			InteractionAction.Superlike => SwipeActionDto.Superlike,
			_ => SwipeActionDto.Skip
		};
	}

	private static async Task<ServiceScope?> TryResolveDefaultRadarrScopeAsync(
		IServiceSettingsRepository settingsRepo,
		CancellationToken cancellationToken)
	{
		var rows = await settingsRepo.ListAsync(ServiceType.Radarr, cancellationToken).ConfigureAwait(false);
		var configured = rows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrBaseUrl))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrApiKey))
			.ToList();

		if (configured.Count == 0)
		{
			return null;
		}

		var preferred = configured.FirstOrDefault(x => string.Equals(x.ServerId, "default", StringComparison.OrdinalIgnoreCase))
			?? configured.First();

		return new ServiceScope(ServiceType.Radarr, preferred.ServerId);
	}
}
