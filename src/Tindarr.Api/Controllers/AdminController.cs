using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;
using Tindarr.Contracts.Admin;
using Tindarr.Contracts.Interactions;
using Tindarr.Contracts.Users;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/admin")]
public sealed class AdminController(
	IUserRepository users,
	IInteractionStore interactionStore,
	IPasswordHasher passwordHasher,
	IOptions<RegistrationOptions> registrationOptions) : ControllerBase
{
	private readonly RegistrationOptions registration = registrationOptions.Value;

	[HttpGet("users")]
	public async Task<ActionResult<IReadOnlyList<UserDto>>> ListUsers([FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
	{
		var list = await users.ListAsync(skip, take, cancellationToken);
		var result = new List<UserDto>(list.Count);

		foreach (var u in list)
		{
			var roles = await users.GetRolesAsync(u.Id, cancellationToken);
			result.Add(new UserDto(u.Id, u.DisplayName, u.CreatedAtUtc, roles.ToList(), u.HasPassword));
		}

		return Ok(result);
	}

	[HttpGet("users/{userId}")]
	public async Task<ActionResult<UserDto>> GetUser([FromRoute] string userId, CancellationToken cancellationToken)
	{
		string id;
		try
		{
			id = NormalizeUserId(userId);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		var user = await users.FindByIdAsync(id, cancellationToken);
		if (user is null)
		{
			return NotFound();
		}

		var roles = await users.GetRolesAsync(id, cancellationToken);
		return Ok(new UserDto(user.Id, user.DisplayName, user.CreatedAtUtc, roles.ToList(), user.HasPassword));
	}

	[HttpPost("users")]
	public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
	{
		string id;
		string displayName;
		try
		{
			id = NormalizeUserId(request.UserId);
			displayName = NormalizeDisplayName(request.DisplayName);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		if (await users.UserExistsAsync(id, cancellationToken))
		{
			return Conflict("User already exists.");
		}

		var now = DateTimeOffset.UtcNow;
		await users.CreateAsync(new CreateUserRecord(id, displayName, now), cancellationToken);

		var hashed = passwordHasher.Hash(request.Password, registration.PasswordHashIterations);
		await users.SetPasswordAsync(id, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);

		var roles = (request.Roles is { Count: > 0 } ? request.Roles : [registration.DefaultRole])
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Select(r => r.Trim())
			.ToList();

		await users.SetRolesAsync(id, roles, cancellationToken);
		var finalRoles = await users.GetRolesAsync(id, cancellationToken);

		return Ok(new UserDto(id, displayName, now, finalRoles.ToList(), HasPassword: true));
	}

	[HttpPut("users/{userId}")]
	public async Task<IActionResult> UpdateUser([FromRoute] string userId, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
	{
		string id;
		string displayName;
		try
		{
			id = NormalizeUserId(userId);
			displayName = NormalizeDisplayName(request.DisplayName);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		await users.UpdateDisplayNameAsync(id, displayName, cancellationToken);
		return NoContent();
	}

	[HttpDelete("users/{userId}")]
	public async Task<IActionResult> DeleteUser([FromRoute] string userId, CancellationToken cancellationToken)
	{
		string id;
		try
		{
			id = NormalizeUserId(userId);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		await users.DeleteAsync(id, cancellationToken);
		return NoContent();
	}

	[HttpPost("users/{userId}/roles")]
	public async Task<IActionResult> SetUserRoles([FromRoute] string userId, [FromBody] SetUserRolesRequest request, CancellationToken cancellationToken)
	{
		string id;
		try
		{
			id = NormalizeUserId(userId);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		await users.SetRolesAsync(id, request.Roles ?? [], cancellationToken);
		return NoContent();
	}

	[HttpPost("users/{userId}/set-password")]
	public async Task<IActionResult> AdminSetPassword([FromRoute] string userId, [FromBody] AdminSetPasswordRequest request, CancellationToken cancellationToken)
	{
		string id;
		try
		{
			id = NormalizeUserId(userId);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		var hashed = passwordHasher.Hash(request.NewPassword, registration.PasswordHashIterations);
		await users.SetPasswordAsync(id, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);
		return NoContent();
	}

	[HttpGet("interactions")]
	public async Task<ActionResult<AdminInteractionSearchResponse>> SearchInteractions(
		[FromQuery] string? userId,
		[FromQuery] string? serviceType,
		[FromQuery] string? serverId,
		[FromQuery] SwipeActionDto? action,
		[FromQuery] int? tmdbId,
		[FromQuery] int limit = 200,
		CancellationToken cancellationToken = default)
	{
		limit = Math.Clamp(limit, 1, 500);

		ServiceScope? scope = null;
		if (!string.IsNullOrWhiteSpace(serviceType) || !string.IsNullOrWhiteSpace(serverId))
		{
			if (!ServiceScope.TryCreate(serviceType ?? string.Empty, serverId ?? string.Empty, out scope))
			{
				return BadRequest("If provided, ServiceType and ServerId must both be valid.");
			}
		}

		InteractionAction? mappedAction = action is null ? null : MapAction(action.Value);
		var items = await interactionStore.SearchAsync(
			string.IsNullOrWhiteSpace(userId) ? null : userId.Trim().ToLowerInvariant(),
			scope,
			mappedAction,
			tmdbId,
			limit,
			cancellationToken);

		return Ok(new AdminInteractionSearchResponse(
			items.Select(x => new AdminInteractionDto(
				x.UserId,
				x.Scope.ServiceType.ToString().ToLowerInvariant(),
				x.Scope.ServerId,
				x.TmdbId,
				MapAction(x.Action),
				x.CreatedAtUtc)).ToList()));
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

	private static string NormalizeUserId(string value)
	{
		var v = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(v))
		{
			throw new ArgumentException("UserId is required.");
		}

		if (v.Any(char.IsWhiteSpace))
		{
			throw new ArgumentException("UserId must not contain whitespace.");
		}

		return v.ToLowerInvariant();
	}

	private static string NormalizeDisplayName(string value)
	{
		var v = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(v))
		{
			throw new ArgumentException("DisplayName is required.");
		}

		return v;
	}
}

