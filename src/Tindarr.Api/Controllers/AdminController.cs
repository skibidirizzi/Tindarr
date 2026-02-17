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
public sealed class AdminController : ControllerBase
{
	private readonly IUserRepository users;
	private readonly IInteractionStore interactionStore;
	private readonly IJoinAddressSettingsRepository joinAddressSettings;
	private readonly ICastingSettingsRepository castingSettings;
	private readonly IServiceSettingsRepository serviceSettings;
	private readonly IPasswordHasher passwordHasher;
	private readonly RegistrationOptions registration;
	private readonly Tindarr.Infrastructure.Casting.CastingSessionStore castingSessionStore;

	public AdminController(
		IUserRepository users,
		IInteractionStore interactionStore,
		IJoinAddressSettingsRepository joinAddressSettings,
		ICastingSettingsRepository castingSettings,
		IServiceSettingsRepository serviceSettings,
		IPasswordHasher passwordHasher,
		IOptions<RegistrationOptions> registrationOptions,
		Tindarr.Infrastructure.Casting.CastingSessionStore castingSessionStore)
	{
		this.users = users;
		this.interactionStore = interactionStore;
		this.joinAddressSettings = joinAddressSettings;
		this.castingSettings = castingSettings;
		this.serviceSettings = serviceSettings;
		this.passwordHasher = passwordHasher;
		registration = registrationOptions.Value;
		this.castingSessionStore = castingSessionStore;
	}

	[HttpGet("casting/diagnostics")]
	public ActionResult<Tindarr.Contracts.Admin.CastingDiagnosticsDto> GetCastingDiagnostics()
	{
		var diagnostics = castingSessionStore.GetDiagnostics();
		return Ok(diagnostics);
	}


	[HttpGet("join-address")]
	public async Task<ActionResult<JoinAddressSettingsDto>> GetJoinAddressSettings(CancellationToken cancellationToken)
	{
		var settings = await joinAddressSettings.GetAsync(cancellationToken);
		if (settings is null)
		{
			return Ok(new JoinAddressSettingsDto(null, null, null, null, DateTimeOffset.UtcNow.ToString("O")));
		}

		return Ok(new JoinAddressSettingsDto(
			settings.LanHostPort,
			settings.WanHostPort,
			settings.RoomLifetimeMinutes,
			settings.GuestSessionLifetimeMinutes,
			settings.UpdatedAtUtc.ToString("O")));
	}

	[HttpPut("join-address")]
	public async Task<ActionResult<JoinAddressSettingsDto>> UpdateJoinAddressSettings([FromBody] UpdateJoinAddressSettingsRequest request, CancellationToken cancellationToken)
	{
		string? lan;
		string? wan;
		try
		{
			lan = NormalizeHostPort(request.LanHostPort, "LanHostPort");
			wan = NormalizeHostPort(request.WanHostPort, "WanHostPort");

			if (request.RoomLifetimeMinutes is not null && request.RoomLifetimeMinutes <= 0)
			{
				throw new ArgumentException("RoomLifetimeMinutes must be null or >= 1.");
			}

			if (request.GuestSessionLifetimeMinutes is not null && request.GuestSessionLifetimeMinutes <= 0)
			{
				throw new ArgumentException("GuestSessionLifetimeMinutes must be null or >= 1.");
			}
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}

		var updated = await joinAddressSettings.UpsertAsync(
			new JoinAddressSettingsUpsert(lan, wan, request.RoomLifetimeMinutes, request.GuestSessionLifetimeMinutes),
			cancellationToken);
		return Ok(new JoinAddressSettingsDto(
			updated.LanHostPort,
			updated.WanHostPort,
			updated.RoomLifetimeMinutes,
			updated.GuestSessionLifetimeMinutes,
			updated.UpdatedAtUtc.ToString("O")));
	}

	[HttpGet("casting")]
	public async Task<ActionResult<CastingSettingsDto>> GetCastingSettings(CancellationToken cancellationToken)
	{
		var settings = await castingSettings.GetAsync(cancellationToken);
		if (settings is null)
		{
			return Ok(new CastingSettingsDto(null, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow.ToString("O")));
		}

		return Ok(new CastingSettingsDto(
			settings.PreferredSubtitleSource,
			settings.PreferredSubtitleLanguage,
			settings.PreferredSubtitleTrackSource,
			settings.SubtitleFallback,
			settings.SubtitleLanguageFallback,
			settings.SubtitleTrackSourceFallback,
			settings.PreferredAudioStyle,
			settings.PreferredAudioLanguage,
			settings.PreferredAudioTrackKind,
			settings.AudioFallback,
			settings.AudioLanguageFallback,
			settings.AudioTrackKindFallback,
			settings.UpdatedAtUtc.ToString("O")));
	}

	[HttpPut("casting")]
	public async Task<ActionResult<CastingSettingsDto>> UpdateCastingSettings([FromBody] UpdateCastingSettingsRequest request, CancellationToken cancellationToken)
	{
		try
		{
			var upsert = new CastingSettingsUpsert(
				PreferredSubtitleSource: NormalizeOption(request.PreferredSubtitleSource, nameof(request.PreferredSubtitleSource)),
				PreferredSubtitleLanguage: NormalizeLanguage(request.PreferredSubtitleLanguage, nameof(request.PreferredSubtitleLanguage)),
				PreferredSubtitleTrackSource: NormalizeOption(request.PreferredSubtitleTrackSource, nameof(request.PreferredSubtitleTrackSource)),
				SubtitleFallback: NormalizeOption(request.SubtitleFallback, nameof(request.SubtitleFallback)),
				SubtitleLanguageFallback: NormalizeLanguage(request.SubtitleLanguageFallback, nameof(request.SubtitleLanguageFallback)),
				SubtitleTrackSourceFallback: NormalizeOption(request.SubtitleTrackSourceFallback, nameof(request.SubtitleTrackSourceFallback)),
				PreferredAudioStyle: NormalizeOption(request.PreferredAudioStyle, nameof(request.PreferredAudioStyle)),
				PreferredAudioLanguage: NormalizeLanguage(request.PreferredAudioLanguage, nameof(request.PreferredAudioLanguage)),
				PreferredAudioTrackKind: NormalizeOption(request.PreferredAudioTrackKind, nameof(request.PreferredAudioTrackKind)),
				AudioFallback: NormalizeOption(request.AudioFallback, nameof(request.AudioFallback)),
				AudioLanguageFallback: NormalizeLanguage(request.AudioLanguageFallback, nameof(request.AudioLanguageFallback)),
				AudioTrackKindFallback: NormalizeOption(request.AudioTrackKindFallback, nameof(request.AudioTrackKindFallback)));

			var updated = await castingSettings.UpsertAsync(upsert, cancellationToken);
			return Ok(new CastingSettingsDto(
				updated.PreferredSubtitleSource,
				updated.PreferredSubtitleLanguage,
				updated.PreferredSubtitleTrackSource,
				updated.SubtitleFallback,
				updated.SubtitleLanguageFallback,
				updated.SubtitleTrackSourceFallback,
				updated.PreferredAudioStyle,
				updated.PreferredAudioLanguage,
				updated.PreferredAudioTrackKind,
				updated.AudioFallback,
				updated.AudioLanguageFallback,
				updated.AudioTrackKindFallback,
				updated.UpdatedAtUtc.ToString("O")));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(ex.Message);
		}
	}

	[HttpGet("matching")]
	public async Task<ActionResult<MatchSettingsDto>> GetMatchSettings(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var settings = await serviceSettings.GetAsync(scope!, cancellationToken).ConfigureAwait(false);
		if (settings is null)
		{
			return Ok(new MatchSettingsDto(
				scope!.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				MinUsers: null,
				MinUserPercent: null,
				UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("O")));
		}

		return Ok(new MatchSettingsDto(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			settings.MatchMinUsers,
			settings.MatchMinUserPercent,
			settings.UpdatedAtUtc.ToString("O")));
	}

	[HttpPut("matching")]
	public async Task<ActionResult<MatchSettingsDto>> UpdateMatchSettings(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromBody] UpdateMatchSettingsRequest request,
		CancellationToken cancellationToken)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		if (request.MinUsers is not null && (request.MinUsers.Value < 1 || request.MinUsers.Value > 50))
		{
			return BadRequest("MinUsers must be between 1 and 50.");
		}

		if (request.MinUserPercent is not null && (request.MinUserPercent.Value < 1 || request.MinUserPercent.Value > 100))
		{
			return BadRequest("MinUserPercent must be between 1 and 100.");
		}

		var existing = await serviceSettings.GetAsync(scope!, cancellationToken).ConfigureAwait(false);
		var upsert = BuildMatchSettingsUpsert(existing, request.MinUsers, request.MinUserPercent);
		await serviceSettings.UpsertAsync(scope!, upsert, cancellationToken).ConfigureAwait(false);

		var updated = await serviceSettings.GetAsync(scope!, cancellationToken).ConfigureAwait(false);
		if (updated is null)
		{
			return StatusCode(StatusCodes.Status500InternalServerError, "Match settings missing after update.");
		}

		return Ok(new MatchSettingsDto(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			updated.MatchMinUsers,
			updated.MatchMinUserPercent,
			updated.UpdatedAtUtc.ToString("O")));
	}

	private static ServiceSettingsUpsert BuildMatchSettingsUpsert(
		ServiceSettingsRecord? existing,
		int? minUsers,
		int? minUserPercent)
	{
		return new ServiceSettingsUpsert(
			RadarrBaseUrl: existing?.RadarrBaseUrl ?? string.Empty,
			RadarrApiKey: existing?.RadarrApiKey ?? string.Empty,
			RadarrQualityProfileId: existing?.RadarrQualityProfileId,
			RadarrRootFolderPath: existing?.RadarrRootFolderPath,
			RadarrTagLabel: existing?.RadarrTagLabel,
			RadarrTagId: existing?.RadarrTagId,
			RadarrAutoAddEnabled: existing?.RadarrAutoAddEnabled ?? false,
			RadarrAutoAddIntervalMinutes: existing?.RadarrAutoAddIntervalMinutes,
			RadarrLastAutoAddRunUtc: existing?.RadarrLastAutoAddRunUtc,
			RadarrLastAutoAddAcceptedId: existing?.RadarrLastAutoAddAcceptedId,
			RadarrLastLibrarySyncUtc: existing?.RadarrLastLibrarySyncUtc,
			MatchMinUsers: minUsers,
			MatchMinUserPercent: minUserPercent,
			JellyfinBaseUrl: existing?.JellyfinBaseUrl,
			JellyfinApiKey: existing?.JellyfinApiKey,
			JellyfinServerName: existing?.JellyfinServerName,
			JellyfinServerVersion: existing?.JellyfinServerVersion,
			JellyfinLastLibrarySyncUtc: existing?.JellyfinLastLibrarySyncUtc,
			EmbyBaseUrl: existing?.EmbyBaseUrl,
			EmbyApiKey: existing?.EmbyApiKey,
			EmbyServerName: existing?.EmbyServerName,
			EmbyServerVersion: existing?.EmbyServerVersion,
			EmbyLastLibrarySyncUtc: existing?.EmbyLastLibrarySyncUtc,
			PlexClientIdentifier: existing?.PlexClientIdentifier,
			PlexAuthToken: existing?.PlexAuthToken,
			PlexServerName: existing?.PlexServerName,
			PlexServerUri: existing?.PlexServerUri,
			PlexServerVersion: existing?.PlexServerVersion,
			PlexServerPlatform: existing?.PlexServerPlatform,
			PlexServerOwned: existing?.PlexServerOwned,
			PlexServerOnline: existing?.PlexServerOnline,
			PlexServerAccessToken: existing?.PlexServerAccessToken,
			PlexLastLibrarySyncUtc: existing?.PlexLastLibrarySyncUtc);
	}

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

	private static string? NormalizeOption(string? value, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var trimmed = value.Trim();
		if (trimmed.Length > 64)
		{
			throw new ArgumentException($"{field} must be 64 characters or less.");
		}

		return trimmed;
	}

	private static string? NormalizeLanguage(string? value, string field)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var trimmed = value.Trim();
		if (trimmed.Length > 16)
		{
			throw new ArgumentException($"{field} must be 16 characters or less.");
		}

		if (trimmed.Any(char.IsWhiteSpace))
		{
			throw new ArgumentException($"{field} must not contain spaces.");
		}

		return trimmed;
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

	private static string? NormalizeHostPort(string? value, string fieldName)
	{
		var v = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(v))
		{
			return null;
		}

		if (v.Contains("://", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException($"{fieldName} must be in the form host:port (no scheme).");
		}

		if (v.Contains('/') || v.Contains('?') || v.Contains('#'))
		{
			throw new ArgumentException($"{fieldName} must be in the form host:port (no path/query/fragment).");
		}

		// Require explicit port.
		if (v.StartsWith('['))
		{
			if (!v.Contains("]:", StringComparison.Ordinal))
			{
				throw new ArgumentException($"{fieldName} must include an explicit port.");
			}
		}
		else
		{
			if (!v.Contains(':', StringComparison.Ordinal))
			{
				throw new ArgumentException($"{fieldName} must include an explicit port.");
			}
		}

		if (!Uri.TryCreate("http://" + v, UriKind.Absolute, out var uri))
		{
			throw new ArgumentException($"{fieldName} is not a valid host:port value.");
		}

		if (string.IsNullOrWhiteSpace(uri.Host))
		{
			throw new ArgumentException($"{fieldName} must include a host.");
		}

		if (uri.Port < 1 || uri.Port > 65535)
		{
			throw new ArgumentException($"{fieldName} must have a valid port (1-65535).");
		}

		return v;
	}
}

