using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Features.Auth;
using Tindarr.Application.Interfaces.Auth;
using Tindarr.Contracts.Auth;

namespace Tindarr.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
	[HttpPost("guest")]
	[AllowAnonymous]
	public async Task<ActionResult<AuthResponse>> Guest([FromBody] GuestLoginRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var session = await authService.GuestAsync(request.RoomId, request.DisplayName, cancellationToken);
			return Ok(Map(session));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			if (string.Equals(ex.Message, "Room is closed to new users.", StringComparison.Ordinal))
			{
				return new ObjectResult(new { message = "This room has already started. Join earlier next time or ask the host for a new room." }) { StatusCode = StatusCodes.Status403Forbidden };
			}

			return BadRequest(ex.Message);
		}
	}

	[HttpPost("register")]
	[AllowAnonymous]
	public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var session = await authService.RegisterAsync(request.UserId, request.DisplayName, request.Password, cancellationToken);
			return Ok(Map(session));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
	}

	[HttpPost("login")]
	[AllowAnonymous]
	public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var session = await authService.LoginAsync(request.UserId, request.Password, cancellationToken);
			return Ok(Map(session));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}

	[HttpGet("me")]
	[Authorize(Policy = Tindarr.Api.Auth.Policies.AllowGuests)]
	public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
	{
		// Guests are not persisted. For guest sessions, return identity from token claims.
		if (User.IsInRole(Tindarr.Api.Auth.Policies.GuestRole))
		{
			var userId = currentUser.UserId;
			var displayName = User.FindFirst(Tindarr.Application.Abstractions.Security.TindarrClaimTypes.DisplayName)?.Value
				?? "Guest";
			return Ok(new MeResponse(userId, displayName, new[] { Tindarr.Api.Auth.Policies.GuestRole }));
		}

		try
		{
			var me = await authService.GetMeAsync(currentUser.UserId, cancellationToken);
			return Ok(new MeResponse(me.UserId, me.DisplayName, me.Roles));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (InvalidOperationException ex) when (ex.Message == "User not found.")
		{
			// Token is valid but user no longer exists (e.g. deleted). Treat as invalid session.
			return Unauthorized();
		}
	}

	[HttpPost("set-password")]
	[Authorize]
	public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest request, CancellationToken cancellationToken)
	{
		try
		{
			await authService.SetPasswordAsync(currentUser.UserId, request.CurrentPassword, request.NewPassword, cancellationToken);
			return NoContent();
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(ex.Message);
		}
	}

	private static AuthResponse Map(AuthSession session)
	{
		return new AuthResponse(session.AccessToken, session.ExpiresAtUtc, session.UserId, session.DisplayName, session.Roles, session.PendingApproval);
	}
}

