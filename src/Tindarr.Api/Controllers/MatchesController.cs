using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Domain;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Contracts.Matching;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/matches")]
public sealed class MatchesController(IInteractionStore interactionStore, IMatchingEngine matchingEngine) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<MatchesResponse>> List(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int minUsers = 2,
		[FromQuery] int interactionLimit = 20_000,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var userId = User.GetUserId();

		// Solo media-server swiping: list everything you personally liked/superliked.
		// This is intentionally not persisted in the main DB (see RoutingInteractionStore).
		if (scope!.ServiceType is ServiceType.Plex or ServiceType.Jellyfin or ServiceType.Emby)
		{
			interactionLimit = Math.Clamp(interactionLimit, 1, 50_000);

			var liked = await interactionStore.ListAsync(userId, scope!, InteractionAction.Like, tmdbId: null, interactionLimit, cancellationToken);
			var superliked = await interactionStore.ListAsync(userId, scope!, InteractionAction.Superlike, tmdbId: null, interactionLimit, cancellationToken);

			var ids = liked
				.Concat(superliked)
				.OrderByDescending(x => x.CreatedAtUtc)
				.Select(x => x.TmdbId)
				.Distinct()
				.ToList();

			return Ok(new MatchesResponse(
				scope!.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				ids.Select(id => new MatchDto(id)).ToList()));
		}

		minUsers = Math.Clamp(minUsers, 2, 50);
		interactionLimit = Math.Clamp(interactionLimit, 1, 50_000);

		var interactions = await interactionStore.ListForScopeAsync(scope!, tmdbId: null, interactionLimit, cancellationToken);
		var tmdbIds = matchingEngine.ComputeLikedByAllMatches(scope!, interactions, minUsers);

		return Ok(new MatchesResponse(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			tmdbIds.Select(id => new MatchDto(id)).ToList()));
	}
}
