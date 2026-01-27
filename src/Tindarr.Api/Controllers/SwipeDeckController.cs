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
[Route("api/v1/swipedeck")]
public sealed class SwipeDeckController(ISwipeDeckService swipeDeckService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SwipeDeckResponse>> Get([FromQuery] string serviceType, [FromQuery] string serverId, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
        {
            return BadRequest("ServiceType and ServerId are required.");
        }

        var userId = User.GetUserId();
        var cards = await swipeDeckService.GetDeckAsync(userId, scope!, Math.Clamp(limit, 1, 50), cancellationToken);
        var response = new SwipeDeckResponse(scope!.ServiceType.ToString().ToLowerInvariant(), scope.ServerId, cards.Select(Map).ToList());

        return Ok(response);
    }

    private static SwipeCardDto Map(SwipeCard card)
    {
        return new SwipeCardDto(card.TmdbId, card.Title, card.Overview, card.PosterUrl, card.BackdropUrl, card.ReleaseYear, card.Rating);
    }
}
