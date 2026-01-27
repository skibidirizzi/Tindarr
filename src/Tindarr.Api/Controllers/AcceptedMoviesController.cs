using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Interfaces.AcceptedMovies;
using Tindarr.Contracts.AcceptedMovies;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/accepted-movies")]
public sealed class AcceptedMoviesController(IAcceptedMoviesService acceptedMoviesService) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<AcceptedMoviesResponse>> List(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int limit = 200,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var items = await acceptedMoviesService.ListAsync(scope!, limit, cancellationToken);

		return Ok(new AcceptedMoviesResponse(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			items.Select(x => new AcceptedMovieDto(x.TmdbId, x.AcceptedByUserId, x.AcceptedAtUtc)).ToList()));
	}

	[HttpPost("force")]
	[Authorize(Policy = Policies.CuratorOnly)]
	public async Task<ActionResult> ForceAccept([FromBody] ForceAcceptMovieRequest request, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		// Curator force-match is modeled as acceptance in the given scope.
		var curatorUserId = User.GetUserId();
		var created = await acceptedMoviesService.ForceAcceptAsync(curatorUserId, scope!, request.TmdbId, cancellationToken);

		return created ? Ok() : Ok(); // idempotent
	}
}
