using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Notifications;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Application.Options;
using Tindarr.Contracts.Interactions;
using Tindarr.Contracts.Rooms;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.RoomAccess)]
[Route("api/v1/rooms")]
public sealed class RoomsController(
	IRoomService roomService,
	IBaseUrlResolver baseUrlResolver,
	IJoinAddressSettingsRepository joinAddressSettings,
	IOptions<BaseUrlOptions> baseUrlOptions,
	ILogger<RoomsController> logger,
	IOutgoingWebhookNotifier webhooks) : ControllerBase
{
	[HttpPost]
	public async Task<ActionResult<CreateRoomResponse>> Create([FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var userId = User.GetUserId();
		try
		{
			var room = await roomService.CreateAsync(userId, scope!, request.RoomName, cancellationToken);
			webhooks.TryNotify(
				OutgoingWebhookEvents.RoomCreated,
				"tindarr.room.created",
				new
				{
					roomId = room.RoomId,
					ownerUserId = room.OwnerUserId,
					createdByUserId = userId,
					scope = new { serviceType = scope!.ServiceType.ToString().ToLowerInvariant(), serverId = scope!.ServerId },
					roomName = request.RoomName,
					createdAtUtc = room.CreatedAtUtc
				},
				room.CreatedAtUtc);
			return Ok(new CreateRoomResponse(
				room.RoomId,
				room.OwnerUserId,
				scope!.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				room.Members.Select(m => new RoomMemberDto(m.UserId, m.JoinedAtUtc.ToString("O"))).ToList()));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Room name already in use.", StringComparison.Ordinal))
		{
			return Conflict(ex.Message);
		}
	}

	[HttpPost("{roomId}/join")]
	public async Task<ActionResult<JoinRoomResponse>> Join([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
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

			if (string.Equals(ex.Message, "Room is closed to new users.", StringComparison.Ordinal))
			{
				return new ObjectResult(new { message = "This room has already started. Join earlier next time or ask the host for a new room." }) { StatusCode = StatusCodes.Status403Forbidden };
			}

			return BadRequest(ex.Message);
		}
	}

	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<RoomListItemDto>>> List(CancellationToken cancellationToken)
	{
		var openOnly = !User.IsInRole(Policies.AdminRole);
		var rooms = await roomService.ListAsync(openOnly, cancellationToken).ConfigureAwait(false);
		var list = rooms
			.Select(r => new RoomListItemDto(
				r.RoomId,
				r.OwnerUserId,
				r.Scope.ServiceType.ToString().ToLowerInvariant(),
				r.Scope.ServerId,
				r.IsClosed,
				r.CreatedAtUtc.ToString("O"),
				r.LastActivityAtUtc.ToString("O"),
				r.Members.Count))
			.ToList();
		return Ok(list);
	}

	[HttpGet("{roomId}")]
	public async Task<ActionResult<RoomStateResponse>> Get([FromRoute] string roomId, CancellationToken cancellationToken)
	{
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
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
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
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
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
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
		var roomSegment = $"/rooms/{room.RoomId}";
		var joinQuery = $"?joinRoom={Uri.EscapeDataString(room.RoomId)}";
		var join = $"{scheme}://{hostPort}{roomSegment}{joinQuery}";

		// Build LAN and WAN URLs for hotswapping when both are configured.
		string? lanUrl = null;
		string? wanUrl = null;
		var normalizedLan = (lanHostPort ?? string.Empty).Trim().TrimEnd('/');
		var normalizedWan = (wanHostPort ?? string.Empty).Trim().TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(normalizedLan))
		{
			var lanScheme = ResolveJoinScheme(normalizedLan, lanHostPort, wanHostPort, baseUrlOptions.Value, Request.Scheme);
			lanUrl = $"{lanScheme}://{normalizedLan}{roomSegment}{joinQuery}";
		}
		if (!string.IsNullOrWhiteSpace(normalizedWan))
		{
			var wanScheme = ResolveJoinScheme(normalizedWan, lanHostPort, wanHostPort, baseUrlOptions.Value, Request.Scheme);
			wanUrl = $"{wanScheme}://{normalizedWan}{roomSegment}{joinQuery}";
		}

		return Ok(new RoomJoinUrlResponse(join, lanUrl, wanUrl));
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
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
		var userId = User.GetUserId();
		// INV-0013: room mode disables superlikes (not compatible with shared selection rules).
		if (request.Action == SwipeActionDto.Superlike)
		{
			return BadRequest("Superlike is not allowed in rooms.");
		}
		try
		{
			var action = MapAction(request.Action);
			var interaction = await roomService.AddInteractionAsync(roomId, userId, request.TmdbId, action, cancellationToken);
			if (action == InteractionAction.Like)
			{
				webhooks.TryNotify(
					OutgoingWebhookEvents.Likes,
					"tindarr.like",
					new
					{
						roomId,
						userId,
						tmdbId = interaction.TmdbId,
						action = interaction.Action,
						createdAtUtc = interaction.CreatedAtUtc
					},
					interaction.CreatedAtUtc);
			}
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
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
		try
		{
			var userId = User.GetUserId();
			var room = await roomService.GetAsync(roomId, cancellationToken);
			if (room is null)
			{
				return NotFound("Room not found.");
			}

			var cards = await roomService.GetSwipeDeckAsync(roomId, userId, Math.Clamp(limit, 10, 50), cancellationToken);
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
		if (EnsureGuestRoomAccess(roomId) is { } forbid)
			return forbid;
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

	/// <summary>Returns Forbid() if the user is a guest and their RoomId claim does not match the requested room; otherwise null.</summary>
	private ActionResult? EnsureGuestRoomAccess(string roomId)
	{
		if (!User.IsInRole(Policies.GuestRole))
			return null;
		// Prefer our claim type; fallback if JWT middleware mapped it (e.g. MapInboundClaims or different handler).
		var claimRoomId = User.FindFirst(TindarrClaimTypes.RoomId)?.Value
			?? User.Claims.FirstOrDefault(c => string.Equals(c.Type, "roomId", StringComparison.OrdinalIgnoreCase) || c.Type.EndsWith("roomId", StringComparison.OrdinalIgnoreCase))?.Value;
		if (string.IsNullOrWhiteSpace(claimRoomId) || !string.Equals(roomId, claimRoomId, StringComparison.OrdinalIgnoreCase))
		{
			logger.LogWarning("Guest room access denied: claimRoomId={ClaimRoomId}, routeRoomId={RouteRoomId}",
				claimRoomId ?? "(none)", roomId);
			return Forbid();
		}
		return null;
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

