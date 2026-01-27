using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Contracts.Interactions;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tmdb")]
public sealed class TmdbController(
	ITmdbClient tmdbClient,
	IUserPreferencesService preferencesService,
	IInteractionStore interactionStore,
	IOptions<TmdbOptions> tmdbOptions) : ControllerBase
{
	[HttpGet("discover")]
	public async Task<ActionResult<SwipeDeckResponse>> Discover(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int limit = 10,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		limit = Math.Clamp(limit, 1, 50);

		if (!tmdbOptions.Value.HasCredentials)
		{
			// TMDB not configured yet on this machine.
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken).");
		}

		var userId = User.GetUserId();
		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken);

		// Pull a bigger pool than requested, then filter seen items.
		var candidatePoolSize = Math.Clamp(limit * 5, 10, 200);
		var candidates = await tmdbClient.DiscoverAsync(prefs, page: 1, candidatePoolSize, cancellationToken);

		var interacted = await interactionStore.GetInteractedTmdbIdsAsync(userId, scope!, cancellationToken);

		var filtered = candidates
			.Where(card => !interacted.Contains(card.TmdbId))
			.Take(limit)
			.ToList();

		return Ok(new SwipeDeckResponse(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			filtered.Select(Map).ToList()));
	}

	[HttpGet("movies/{tmdbId:int}")]
	public async Task<ActionResult<MovieDetailsDto>> GetMovieDetails(
		[FromRoute] int tmdbId,
		CancellationToken cancellationToken = default)
	{
		if (!tmdbOptions.Value.HasCredentials)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, "TMDB is not configured (missing Tmdb__ApiKey or Tmdb__ReadAccessToken).");
		}

		var details = await tmdbClient.GetMovieDetailsAsync(tmdbId, cancellationToken);
		if (details is null)
		{
			return NotFound();
		}

		return Ok(details);
	}

	private static SwipeCardDto Map(SwipeCard card)
	{
		return new SwipeCardDto(card.TmdbId, card.Title, card.Overview, card.PosterUrl, card.BackdropUrl, card.ReleaseYear, card.Rating);
	}
}

