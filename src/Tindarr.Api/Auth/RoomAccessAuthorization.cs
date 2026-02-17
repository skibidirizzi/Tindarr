using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Tindarr.Application.Abstractions.Security;

namespace Tindarr.Api.Auth;

public sealed class RoomAccessRequirement : IAuthorizationRequirement;

public sealed class RoomAccessAuthorizationHandler : AuthorizationHandler<RoomAccessRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoomAccessRequirement requirement)
	{
		if (context.User?.Identity?.IsAuthenticated != true)
		{
			return Task.CompletedTask;
		}

		// Non-guest users are allowed.
		if (!context.User.IsInRole(Policies.GuestRole))
		{
			context.Succeed(requirement);
			return Task.CompletedTask;
		}

		// Guest users must be tied to a specific room.
		var claimRoomId = context.User.FindFirst(TindarrClaimTypes.RoomId)?.Value;
		if (string.IsNullOrWhiteSpace(claimRoomId))
		{
			return Task.CompletedTask;
		}

		if (context.Resource is not HttpContext http)
		{
			return Task.CompletedTask;
		}

		if (!http.Request.RouteValues.TryGetValue("roomId", out var routeRoomObj))
		{
			return Task.CompletedTask;
		}

		var routeRoomId = Convert.ToString(routeRoomObj);
		if (string.IsNullOrWhiteSpace(routeRoomId))
		{
			return Task.CompletedTask;
		}

		if (string.Equals(routeRoomId, claimRoomId, StringComparison.Ordinal))
		{
			context.Succeed(requirement);
		}

		return Task.CompletedTask;
	}
}
