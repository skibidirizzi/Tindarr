using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Contracts.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/interactions")]
public sealed class InteractionsController(IInteractionService interactionService) : ControllerBase
{
	[HttpPost]
	public async Task<ActionResult<SwipeResponse>> Create([FromBody] SwipeRequest request, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var action = MapAction(request.Action);
		var userId = User.GetUserId();
		var interaction = await interactionService.AddAsync(userId, scope!, request.TmdbId, action, cancellationToken);

		return Ok(new SwipeResponse(interaction.TmdbId, MapAction(interaction.Action), interaction.CreatedAtUtc));
	}

	[HttpPost("undo")]
	public async Task<ActionResult<UndoResponse>> Undo([FromQuery] string serviceType, [FromQuery] string serverId, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var userId = User.GetUserId();
		var interaction = await interactionService.UndoLastAsync(userId, scope!, cancellationToken);

		if (interaction is null)
		{
			return Ok(new UndoResponse(false, null, null, null));
		}

		return Ok(new UndoResponse(true, interaction.TmdbId, MapAction(interaction.Action), interaction.CreatedAtUtc));
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
}
