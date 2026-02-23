using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Security;

namespace Tindarr.Api.Auth;

public sealed class RoomAccessRequirement : IAuthorizationRequirement;

/// <summary>
/// Allows authenticated users. Guests must have a RoomId claim; binding to the requested room
/// is enforced in RoomsController so route values are always available there.
/// </summary>
public sealed class RoomAccessAuthorizationHandler(ILogger<RoomAccessAuthorizationHandler> logger) : AuthorizationHandler<RoomAccessRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoomAccessRequirement requirement)
	{
		if (context.User?.Identity?.IsAuthenticated != true)
		{
			return Task.CompletedTask;
		}

		// Non-guest users are allowed for any room endpoint.
		if (!context.User.IsInRole(Policies.GuestRole))
		{
			context.Succeed(requirement);
			return Task.CompletedTask;
		}

		// Guest users must have a RoomId claim. Match against the requested room is enforced in RoomsController.
		// Fallback if JWT middleware mapped the claim type (e.g. before MapInboundClaims = false).
		var claimRoomId = context.User.FindFirst(TindarrClaimTypes.RoomId)?.Value
			?? context.User.Claims.FirstOrDefault(c => string.Equals(c.Type, "roomId", StringComparison.OrdinalIgnoreCase) || c.Type.EndsWith("roomId", StringComparison.OrdinalIgnoreCase))?.Value;
		if (!string.IsNullOrWhiteSpace(claimRoomId))
		{
			context.Succeed(requirement);
		}
		else
		{
			logger.LogWarning("RoomAccess denied for guest: no RoomId claim. ClaimTypes present: {Types}",
				string.Join(", ", context.User.Claims.Select(c => c.Type)));
		}

		return Task.CompletedTask;
	}
}
