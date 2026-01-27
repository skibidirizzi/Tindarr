using Microsoft.AspNetCore.Authorization;
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
		catch (InvalidOperationException)
		{
			// Avoid leaking whether the user exists.
			return BadRequest("Invalid credentials.");
		}
	}

	[HttpGet("me")]
	[Authorize]
	public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
	{
		var me = await authService.GetMeAsync(currentUser.UserId, cancellationToken);
		return Ok(new MeResponse(me.UserId, me.DisplayName, me.Roles));
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
		return new AuthResponse(session.AccessToken, session.ExpiresAtUtc, session.UserId, session.DisplayName, session.Roles);
	}
}

