using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Application.Options;
using Tindarr.Contracts.Interactions;
using Tindarr.Contracts.Rooms;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms")]
public sealed class RoomsController(
	IRoomService roomService,
	IBaseUrlResolver baseUrlResolver,
	IJoinAddressSettingsRepository joinAddressSettings,
	IOptions<BaseUrlOptions> baseUrlOptions) : ControllerBase
{
	[HttpPost]
	public async Task<ActionResult<CreateRoomResponse>> Create([FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var userId = User.GetUserId();
		var room = await roomService.CreateAsync(userId, scope!, cancellationToken);
		return Ok(new CreateRoomResponse(
			room.RoomId,
			room.OwnerUserId,
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			room.Members.Select(m => new RoomMemberDto(m.UserId, m.JoinedAtUtc.ToString("O"))).ToList()));
	}

	[HttpPost("{roomId}/join")]
	public async Task<ActionResult<JoinRoomResponse>> Join([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		try
		{
			var userId = User.GetUserId();
			var room = await roomService.JoinAsync(roomId, userId, cancellationToken);
			return Ok(new JoinRoomResponse(
				room.RoomId,
				room.OwnerUserId,
				room.Scope.ServiceType.ToString().ToLowerInvariant(),
				room.Scope.ServerId,
				room.Members.Select(m => new RoomMemberDto(m.UserId, m.JoinedAtUtc.ToString("O"))).ToList()));
		}
		catch (InvalidOperationException ex)
		{
			if (string.Equals(ex.Message, "Room not found.", StringComparison.Ordinal))
			{
				return NotFound(ex.Message);
			}

			return BadRequest(ex.Message);
		}
	}

	[HttpGet("{roomId}")]
	public async Task<ActionResult<RoomStateResponse>> Get([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		var room = await roomService.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			return NotFound("Room not found.");
		}

		return Ok(new RoomStateResponse(
			room.RoomId,
			room.OwnerUserId,
			room.Scope.ServiceType.ToString().ToLowerInvariant(),
			room.Scope.ServerId,
			room.IsClosed,
			room.CreatedAtUtc.ToString("O"),
			room.LastActivityAtUtc.ToString("O"),
			room.Members.Select(m => new RoomMemberDto(m.UserId, m.JoinedAtUtc.ToString("O"))).ToList()));
	}

	[HttpPost("{roomId}/close")]
	public async Task<ActionResult<RoomStateResponse>> Close([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		try
		{
			var userId = User.GetUserId();
			var room = await roomService.CloseAsync(roomId, userId, cancellationToken);
			return Ok(new RoomStateResponse(
				room.RoomId,
				room.OwnerUserId,
				room.Scope.ServiceType.ToString().ToLowerInvariant(),
				room.Scope.ServerId,
				room.IsClosed,
				room.CreatedAtUtc.ToString("O"),
				room.LastActivityAtUtc.ToString("O"),
				room.Members.Select(m => new RoomMemberDto(m.UserId, m.JoinedAtUtc.ToString("O"))).ToList()));
		}
		catch (InvalidOperationException ex)
		{
			if (string.Equals(ex.Message, "Room not found.", StringComparison.Ordinal))
			{
				return NotFound(ex.Message);
			}

			return BadRequest(ex.Message);
		}
	}

	[HttpGet("{roomId}/join-url")]
	public async Task<ActionResult<RoomJoinUrlResponse>> GetJoinUrl([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		var room = await roomService.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			return NotFound("Room not found.");
		}

		var settings = await joinAddressSettings.GetAsync(cancellationToken);
		var lanHostPort = settings?.LanHostPort;
		var wanHostPort = settings?.WanHostPort;

		if (string.IsNullOrWhiteSpace(lanHostPort) && string.IsNullOrWhiteSpace(wanHostPort))
		{
			return BadRequest("Join URL base is not configured. Ask an admin to set LAN/WAN join address in Admin Console.");
		}

		var clientIp = HttpContext.Connection.RemoteIpAddress;
		var mode = baseUrlOptions.Value.Mode;
		var useLan = mode switch
		{
			BaseUrlMode.ForceLan => true,
			BaseUrlMode.ForceWan => false,
			BaseUrlMode.Auto => clientIp is not null && baseUrlResolver.IsLanClient(clientIp),
			_ => false
		};

		var hostPort = useLan ? lanHostPort : wanHostPort;
		if (string.IsNullOrWhiteSpace(hostPort))
		{
			// Fallback to the other if only one is configured.
			hostPort = useLan ? wanHostPort : lanHostPort;
		}

		hostPort = (hostPort ?? string.Empty).Trim().TrimEnd('/');
		var scheme = ResolveJoinScheme(
			hostPort,
			lanHostPort,
			wanHostPort,
			baseUrlOptions.Value,
			Request.Scheme);
		var join = $"{scheme}://{hostPort}/rooms/{room.RoomId}";
		return Ok(new RoomJoinUrlResponse(join));
	}

	private static string ResolveJoinScheme(
		string hostPort,
		string? lanHostPort,
		string? wanHostPort,
		BaseUrlOptions options,
		string? requestScheme)
	{
		var normalizedHostPort = (hostPort ?? string.Empty).Trim();
		var normalizedLan = (lanHostPort ?? string.Empty).Trim();
		var normalizedWan = (wanHostPort ?? string.Empty).Trim();

		string? preferredBase = null;
		if (!string.IsNullOrWhiteSpace(normalizedLan)
			&& string.Equals(normalizedHostPort, normalizedLan, StringComparison.OrdinalIgnoreCase))
		{
			preferredBase = options.Lan;
		}
		else if (!string.IsNullOrWhiteSpace(normalizedWan)
			&& string.Equals(normalizedHostPort, normalizedWan, StringComparison.OrdinalIgnoreCase))
		{
			preferredBase = options.Wan;
		}

		if (!string.IsNullOrWhiteSpace(preferredBase)
			&& Uri.TryCreate(preferredBase.Trim(), UriKind.Absolute, out var baseUri)
			&& baseUri.Scheme is "http" or "https")
		{
			return baseUri.Scheme;
		}

		// Heuristic fallback: choose scheme by port.
		if (Uri.TryCreate("http://" + normalizedHostPort, UriKind.Absolute, out var parsed))
		{
			return parsed.Port == 443 ? "https" : "http";
		}

		// Final fallback: use the API request scheme if it is http/https.
		if (string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase))
		{
			return "https";
		}

		return "http";
	}

	[HttpPost("{roomId}/swipe")]
	public async Task<ActionResult<RoomSwipeResponse>> Swipe([FromRoute] string roomId, [FromBody] RoomSwipeRequest request, CancellationToken cancellationToken)
	{
		var userId = User.GetUserId();
		try
		{
			var action = MapAction(request.Action);
			var interaction = await roomService.AddInteractionAsync(roomId, userId, request.TmdbId, action, cancellationToken);
			return Ok(new RoomSwipeResponse(interaction.TmdbId, MapAction(interaction.Action), interaction.CreatedAtUtc.ToString("O")));
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
	}

	[HttpGet("{roomId}/swipedeck")]
	public async Task<ActionResult<SwipeDeckResponse>> SwipeDeck([FromRoute] string roomId, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
	{
		try
		{
			var userId = User.GetUserId();
			var room = await roomService.GetAsync(roomId, cancellationToken);
			if (room is null)
			{
				return NotFound("Room not found.");
			}

			var cards = await roomService.GetSwipeDeckAsync(roomId, userId, Math.Clamp(limit, 1, 50), cancellationToken);
			var response = new SwipeDeckResponse(
				room.Scope.ServiceType.ToString().ToLowerInvariant(),
				room.Scope.ServerId,
				cards.Select(Map).ToList());
			return Ok(response);
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (HttpRequestException ex)
		{
			return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout, "Upstream request timed out.");
		}
	}

	[HttpGet("{roomId}/matches")]
	public async Task<ActionResult<RoomMatchesResponse>> Matches([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		try
		{
			var room = await roomService.GetAsync(roomId, cancellationToken);
			if (room is null)
			{
				return NotFound("Room not found.");
			}

			var ids = await roomService.ListMatchesAsync(roomId, cancellationToken);
			return Ok(new RoomMatchesResponse(
				room.RoomId,
				room.Scope.ServiceType.ToString().ToLowerInvariant(),
				room.Scope.ServerId,
				ids.ToList()));
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
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

	private static SwipeCardDto Map(SwipeCard card)
	{
		return new SwipeCardDto(card.TmdbId, card.Title, card.Overview, card.PosterUrl, card.BackdropUrl, card.ReleaseYear, card.Rating);
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

