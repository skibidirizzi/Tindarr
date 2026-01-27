using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Domain;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Contracts.Matching;
using Tindarr.Domain.Common;

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
