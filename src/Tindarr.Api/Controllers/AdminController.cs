using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Notifications;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;
using Tindarr.Contracts.Admin;
using Tindarr.Contracts.Interactions;
using Tindarr.Contracts.Users;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Persistence;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/v1/admin")]
public sealed class AdminController(
	IUserRepository users,
	TindarrDbContext db,
	IJoinAddressSettingsRepository joinAddressSettings,
	ICastingSettingsRepository castingSettings,
	IServiceSettingsRepository serviceSettings,
	IAdvancedSettingsRepository advancedSettings,
	IEffectiveAdvancedSettings effectiveAdvancedSettings,
	IPasswordHasher passwordHasher,
	IOptions<RegistrationOptions> registrationOptions,
	IEffectiveRegistrationOptions effectiveRegistration,
	IRegistrationSettingsRepository registrationSettings,
	Tindarr.Infrastructure.Casting.CastingSessionStore castingSessionStore,
	IConsoleOutputCapture consoleOutputCapture,
	IOutgoingWebhookNotifier webhooks) : ControllerBase
{
	private static readonly AdvancedSettingsApiRateLimitDto ApiRateLimitDefaults = new(
		Enabled: true,
		PermitLimit: 200,
		WindowMinutes: 1);

	private static readonly AdvancedSettingsCleanupDto CleanupDefaults = new(
		Enabled: true,
		IntervalMinutes: (int)TimeSpan.FromHours(6).TotalMinutes,
		PurgeGuestUsers: true,
		GuestUserMaxAgeHours: (int)TimeSpan.FromDays(1).TotalHours);

	private static readonly AdvancedSettingsNotificationsDto NotificationsDefaults = new(
		Enabled: false,
		WebhookUrls: Array.Empty<string>(),
		Events: new AdvancedSettingsNotificationsEventsDto(
			Likes: false,
			Matches: false,
			RoomCreated: false,
			Login: false,
			UserCreated: false,
			AuthFailures: false));

	private static readonly AdvancedSettingsDisplayDto DisplayDefaults = new(
		DateTimeDisplayMode: "locale",
		TimeZoneId: "Local",
		DateOrder: "locale");

	[HttpGet("console")]
	public ActionResult<ConsoleOutputDto> GetConsoleOutput([FromQuery] int maxLines = 500)
	{
		maxLines = Math.Clamp(maxLines, 1, 2000);
		var lines = consoleOutputCapture.GetRecentLines(maxLines);
		return Ok(new ConsoleOutputDto(lines));
	}

	[HttpGet("registration")]
	public ActionResult<RegistrationSettingsDto> GetRegistrationSettings()
	{
		return Ok(new RegistrationSettingsDto(
			effectiveRegistration.AllowOpenRegistration,
			effectiveRegistration.RequireAdminApprovalForNewUsers,
			effectiveRegistration.DefaultRole));
	}

	[HttpPut("registration")]
	public async Task<ActionResult<RegistrationSettingsDto>> UpdateRegistrationSettings(
		[FromBody] UpdateRegistrationSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var defaultRole = string.IsNullOrWhiteSpace(request.DefaultRole) ? null : request.DefaultRole.Trim();
		if (defaultRole is not null && defaultRole.Length > 64)
		{
			return BadRequest("DefaultRole must be at most 64 characters.");
		}

		// Require admin approval only applies when open registration is on; disallow unsupported combination.
		var requireApproval = request.AllowOpenRegistration && request.RequireAdminApprovalForNewUsers;

		var record = await registrationSettings.UpsertAsync(new RegistrationSettingsUpsert(
			request.AllowOpenRegistration,
			requireApproval,
			defaultRole), cancellationToken);
		effectiveRegistration.Invalidate();

		return Ok(new RegistrationSettingsDto(
			record.AllowOpenRegistration ?? registrationOptions.Value.AllowOpenRegistration,
			record.RequireAdminApprovalForNewUsers ?? registrationOptions.Value.RequireAdminApprovalForNewUsers,
			!string.IsNullOrWhiteSpace(record.DefaultRole) ? record.DefaultRole! : registrationOptions.Value.DefaultRole));
	}

	[HttpGet("advanced-settings")]
	public Task<ActionResult<AdvancedSettingsDto>> GetAdvancedSettings(CancellationToken cancellationToken)
	{
		var api = effectiveAdvancedSettings.GetApiRateLimitOptions();
		var cleanup = effectiveAdvancedSettings.GetCleanupOptions();
		var webhooks = effectiveAdvancedSettings.GetOutgoingWebhookSettings();
		var hasTmdbApiKey = !string.IsNullOrWhiteSpace(effectiveAdvancedSettings.GetEffectiveTmdbApiKey());
		var hasTmdbReadAccessToken = !string.IsNullOrWhiteSpace(effectiveAdvancedSettings.GetEffectiveTmdbReadAccessToken());
		var dateTimeDisplayMode = effectiveAdvancedSettings.GetDateTimeDisplayMode();
		var timeZoneId = effectiveAdvancedSettings.GetTimeZoneId();
		var dateOrder = effectiveAdvancedSettings.GetDateOrder();

		var dto = new AdvancedSettingsDto(
			ApiRateLimit: new AdvancedSettingsApiRateLimitDto(api.Enabled, api.PermitLimit, (int)api.Window.TotalMinutes),
			ApiRateLimitDefaults,
			Cleanup: new AdvancedSettingsCleanupDto(
				cleanup.Enabled,
				(int)cleanup.Interval.TotalMinutes,
				cleanup.PurgeGuestUsers,
				(int)cleanup.GuestUserMaxAge.TotalHours),
			CleanupDefaults,
			Notifications: new AdvancedSettingsNotificationsDto(
				webhooks.Enabled,
				webhooks.Urls,
				new AdvancedSettingsNotificationsEventsDto(
					Likes: webhooks.Events.HasFlag(OutgoingWebhookEvents.Likes),
					Matches: webhooks.Events.HasFlag(OutgoingWebhookEvents.Matches),
					RoomCreated: webhooks.Events.HasFlag(OutgoingWebhookEvents.RoomCreated),
					Login: webhooks.Events.HasFlag(OutgoingWebhookEvents.Login),
					UserCreated: webhooks.Events.HasFlag(OutgoingWebhookEvents.UserCreated),
					AuthFailures: webhooks.Events.HasFlag(OutgoingWebhookEvents.AuthFailures))),
			NotificationsDefaults,
			Tmdb: new AdvancedSettingsTmdbDto(hasTmdbApiKey, hasTmdbReadAccessToken),
			Display: new AdvancedSettingsDisplayDto(dateTimeDisplayMode, timeZoneId, dateOrder),
			DisplayDefaults);
		return Task.FromResult<ActionResult<AdvancedSettingsDto>>(Ok(dto));
	}

	[HttpPut("advanced-settings")]
	public async Task<ActionResult<AdvancedSettingsDto>> UpdateAdvancedSettings(
		[FromBody] UpdateAdvancedSettingsRequest request,
		CancellationToken cancellationToken)
	{
		if (request.ApiRateLimitPermitLimit is int pl && (pl < 1 || pl > 10_000))
		{
			return BadRequest("ApiRateLimitPermitLimit must be between 1 and 10000.");
		}

		if (request.ApiRateLimitWindowMinutes is int wm && (wm < 1 || wm > 60 * 24))
		{
			return BadRequest("ApiRateLimitWindowMinutes must be between 1 and 1440.");
		}

		if (request.CleanupIntervalMinutes is int ci && (ci < 1 || ci > 60 * 24 * 7))
		{
			return BadRequest("CleanupIntervalMinutes must be between 1 and 10080.");
		}

		if (request.CleanupGuestUserMaxAgeHours is int gh && (gh < 1 || gh > 24 * 365))
		{
			return BadRequest("CleanupGuestUserMaxAgeHours must be between 1 and 8760.");
		}

		string? dateTimeDisplayMode = null;
		if (request.DateTimeDisplayMode is not null)
		{
			var v = request.DateTimeDisplayMode.Trim().ToLowerInvariant();
			if (v is not ("locale" or "12h" or "24h" or "relative"))
			{
				return BadRequest("DateTimeDisplayMode must be one of: locale, 12h, 24h, relative.");
			}
			dateTimeDisplayMode = v;
		}

		string? timeZoneId = request.TimeZoneId?.Trim();
		if (timeZoneId is not null && timeZoneId.Length == 0)
		{
			timeZoneId = null;
		}

		string? dateOrder = null;
		if (request.DateOrder is not null)
		{
			var v = request.DateOrder.Trim().ToLowerInvariant();
			if (v is not ("locale" or "mdy" or "dmy" or "ymd"))
			{
				return BadRequest("DateOrder must be one of: locale, mdy, dmy, ymd.");
			}
			dateOrder = v;
		}

		bool? notificationsEnabledForUpsert = null;
		string? notificationsWebhookUrlsJsonForUpsert = null;
		int? notificationsEventsMaskForUpsert = null;
		if (request.NotificationsSet == true)
		{
			notificationsEnabledForUpsert = request.NotificationsEnabled ?? false;
			var urls = (request.NotificationsWebhookUrls ?? Array.Empty<string>())
				.Select(x => (x ?? string.Empty).Trim())
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (urls.Count > 20)
			{
				return BadRequest("At most 20 webhook URLs are allowed.");
			}

			foreach (var url in urls)
			{
				if (url.Length > 2048)
				{
					return BadRequest("Webhook URL must be at most 2048 characters.");
				}
				if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
				{
					return BadRequest("Webhook URL must be an absolute http/https URL.");
				}
			}

			notificationsWebhookUrlsJsonForUpsert = urls.Count == 0
				? null
				: JsonSerializer.Serialize(urls);

			OutgoingWebhookEvents mask = OutgoingWebhookEvents.None;
			if (request.NotificationsEventLikes == true) mask |= OutgoingWebhookEvents.Likes;
			if (request.NotificationsEventMatches == true) mask |= OutgoingWebhookEvents.Matches;
			if (request.NotificationsEventRoomCreated == true) mask |= OutgoingWebhookEvents.RoomCreated;
			if (request.NotificationsEventLogin == true) mask |= OutgoingWebhookEvents.Login;
			if (request.NotificationsEventUserCreated == true) mask |= OutgoingWebhookEvents.UserCreated;
			if (request.NotificationsEventAuthFailures == true) mask |= OutgoingWebhookEvents.AuthFailures;
			notificationsEventsMaskForUpsert = (int)mask;
		}

		// Only update TmdbApiKey when client explicitly sets it (TmdbApiKeySet == true); otherwise keep existing.
		string? tmdbApiKeyForUpsert = request.TmdbApiKeySet == true
			? (string.IsNullOrWhiteSpace(request.TmdbApiKey) ? null : request.TmdbApiKey!.Trim())
			: null;
		// Only update TmdbReadAccessToken when client explicitly sets it (TmdbReadAccessTokenSet == true); otherwise keep existing.
		string? tmdbReadAccessTokenForUpsert = request.TmdbReadAccessTokenSet == true
			? (string.IsNullOrWhiteSpace(request.TmdbReadAccessToken) ? null : request.TmdbReadAccessToken!.Trim())
			: null;
		AdvancedSettingsRecord? existingRecord = null;
		if (tmdbApiKeyForUpsert is null && request.TmdbApiKeySet != true
			|| tmdbReadAccessTokenForUpsert is null && request.TmdbReadAccessTokenSet != true
			|| dateTimeDisplayMode is null
			|| timeZoneId is null
			|| dateOrder is null
			|| request.NotificationsSet != true)
		{
			existingRecord = await advancedSettings.GetAsync(cancellationToken).ConfigureAwait(false);
			if (tmdbApiKeyForUpsert is null && request.TmdbApiKeySet != true)
			{
				tmdbApiKeyForUpsert = existingRecord?.TmdbApiKey;
			}
			if (tmdbReadAccessTokenForUpsert is null && request.TmdbReadAccessTokenSet != true)
			{
				tmdbReadAccessTokenForUpsert = existingRecord?.TmdbReadAccessToken;
			}
			if (dateTimeDisplayMode is null)
			{
				dateTimeDisplayMode = existingRecord?.DateTimeDisplayMode;
			}
			if (timeZoneId is null)
			{
				timeZoneId = existingRecord?.TimeZoneId;
			}
			if (dateOrder is null)
			{
				dateOrder = existingRecord?.DateOrder;
			}
			if (request.NotificationsSet != true)
			{
				notificationsEnabledForUpsert = existingRecord?.NotificationsEnabled;
				notificationsWebhookUrlsJsonForUpsert = existingRecord?.NotificationsWebhookUrlsJson;
				notificationsEventsMaskForUpsert = existingRecord?.NotificationsEventsMask;
			}
		}

		var upsert = new AdvancedSettingsUpsert(
			request.ApiRateLimitEnabled,
			request.ApiRateLimitPermitLimit,
			request.ApiRateLimitWindowMinutes,
			request.CleanupEnabled,
			request.CleanupIntervalMinutes,
			request.CleanupPurgeGuestUsers,
			request.CleanupGuestUserMaxAgeHours,
			notificationsEnabledForUpsert,
			notificationsWebhookUrlsJsonForUpsert,
			notificationsEventsMaskForUpsert,
			tmdbApiKeyForUpsert,
			tmdbReadAccessTokenForUpsert,
			dateTimeDisplayMode,
			timeZoneId,
			dateOrder);
		await advancedSettings.UpsertAsync(upsert, cancellationToken).ConfigureAwait(false);
		effectiveAdvancedSettings.Invalidate();

		return await GetAdvancedSettings(cancellationToken).ConfigureAwait(false);
	}

	[HttpGet("casting/diagnostics")]
	public ActionResult<Tindarr.Contracts.Admin.CastingDiagnosticsDto> GetCastingDiagnostics()
	{
		var diagnostics = castingSessionStore.GetDiagnostics();
		return Ok(diagnostics);
	}

	[HttpGet("casting/diagnostics/stream")]
	public async Task GetCastingDiagnosticsStream(CancellationToken cancellationToken)
	{
		Response.ContentType = "text/event-stream";
		Response.Headers.CacheControl = "no-cache";
		Response.Headers.Connection = "keep-alive";
		Response.Headers["X-Accel-Buffering"] = "no";

		var jsonOptions = HttpContext.RequestServices
			.GetRequiredService<IOptions<JsonOptions>>()
			.Value
			.JsonSerializerOptions;

		var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false
		});

		void SignalSnapshot() => channel.Writer.TryWrite(0);

		EventHandler<Tindarr.Infrastructure.Casting.CastingEventArgs> onChanged = (_, __) => SignalSnapshot();
		castingSessionStore.SessionStarted += onChanged;
		castingSessionStore.SessionEnded += onChanged;
		castingSessionStore.SessionError += onChanged;

		static async Task WriteComment(HttpResponse response, string text, CancellationToken ct)
		{
			await response.WriteAsync($": {text}\n\n", ct);
			await response.Body.FlushAsync(ct);
		}

		static async Task WriteEvent(HttpResponse response, string eventName, string json, CancellationToken ct)
		{
			await response.WriteAsync($"event: {eventName}\n", ct);
			await response.WriteAsync($"data: {json}\n\n", ct);
			await response.Body.FlushAsync(ct);
		}

		async Task WriteSnapshot(CancellationToken ct)
		{
			var snapshot = castingSessionStore.GetDiagnostics();
			var json = JsonSerializer.Serialize(snapshot, jsonOptions);
			await WriteEvent(Response, eventName: "diagnostics", json, ct);
		}

		var keepAliveInterval = TimeSpan.FromSeconds(15);
		var periodicSnapshotInterval = TimeSpan.FromSeconds(30);
		var lastSnapshotAt = DateTimeOffset.MinValue;

		try
		{
			await WriteSnapshot(cancellationToken);
			lastSnapshotAt = DateTimeOffset.UtcNow;

			while (!cancellationToken.IsCancellationRequested)
			{
				var delayTask = Task.Delay(keepAliveInterval, cancellationToken);
				var readTask = channel.Reader.ReadAsync(cancellationToken).AsTask();
				var completed = await Task.WhenAny(delayTask, readTask).ConfigureAwait(false);

				if (completed == delayTask)
				{
					await WriteComment(Response, "keepalive", cancellationToken);
					if (DateTimeOffset.UtcNow - lastSnapshotAt >= periodicSnapshotInterval)
					{
						await WriteSnapshot(cancellationToken);
						lastSnapshotAt = DateTimeOffset.UtcNow;
					}
					continue;
				}

				// Drain any queued signals so we coalesce multiple events into one snapshot.
				while (channel.Reader.TryRead(out _)) { }

				await WriteSnapshot(cancellationToken);
				lastSnapshotAt = DateTimeOffset.UtcNow;
			}
		}
		catch (OperationCanceledException)
		{
			// client disconnected
		}
		finally
		{
			castingSessionStore.SessionStarted -= onChanged;
			castingSessionStore.SessionEnded -= onChanged;
			castingSessionStore.SessionError -= onChanged;
		}
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

		var hashed = passwordHasher.Hash(request.Password, registrationOptions.Value.PasswordHashIterations);
		await users.SetPasswordAsync(id, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);

		var roles = (request.Roles is { Count: > 0 } ? request.Roles : [effectiveRegistration.DefaultRole])
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Select(r => r.Trim())
			.ToList();

		await users.SetRolesAsync(id, roles, cancellationToken);
		var finalRoles = await users.GetRolesAsync(id, cancellationToken);

		webhooks.TryNotify(
			OutgoingWebhookEvents.UserCreated,
			"tindarr.user.created",
			new
			{
				userId = id,
				displayName,
				roles = finalRoles,
				createdByUserId = User.GetUserId(),
				createdAtUtc = now
			},
			now);

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

		var hashed = passwordHasher.Hash(request.NewPassword, registrationOptions.Value.PasswordHashIterations);
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
		[FromQuery] DateTimeOffset? sinceUtc,
		[FromQuery] int limit = 200,
		CancellationToken cancellationToken = default)
	{
		limit = Math.Clamp(limit, 1, 5000);

		ServiceScope? scope = null;
		if (!string.IsNullOrWhiteSpace(serviceType) || !string.IsNullOrWhiteSpace(serverId))
		{
			if (!ServiceScope.TryCreate(serviceType ?? string.Empty, serverId ?? string.Empty, out scope))
			{
				return BadRequest("If provided, ServiceType and ServerId must both be valid.");
			}
		}

		InteractionAction? mappedAction = action is null ? null : MapAction(action.Value);

		var query = db.Interactions.AsNoTracking().AsQueryable();

		var normalizedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim().ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(normalizedUserId))
		{
			query = query.Where(x => x.UserId == normalizedUserId);
		}

		if (scope is not null)
		{
			query = query.Where(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId);
		}

		if (mappedAction is not null)
		{
			query = query.Where(x => x.Action == mappedAction.Value);
		}

		if (tmdbId is not null)
		{
			query = query.Where(x => x.TmdbId == tmdbId.Value);
		}

		if (sinceUtc is not null)
		{
			query = query.Where(x => x.CreatedAtUtc >= sinceUtc.Value);
		}

		var items = await query
			.OrderByDescending(x => x.Id)
			.Take(limit)
			.ToListAsync(cancellationToken);

		return Ok(new AdminInteractionSearchResponse(
			items.Select(x => new AdminInteractionDto(
				x.Id,
				x.UserId,
				x.ServiceType.ToString().ToLowerInvariant(),
				x.ServerId,
				x.TmdbId,
				MapAction(x.Action),
				x.CreatedAtUtc)).ToList()));
	}

	[HttpDelete("interactions/{id:long}")]
	public async Task<IActionResult> DeleteInteraction([FromRoute] long id, CancellationToken cancellationToken)
	{
		var deleted = await db.Interactions
			.Where(x => x.Id == id)
			.ExecuteDeleteAsync(cancellationToken);

		return deleted == 0 ? NotFound() : NoContent();
	}

	[HttpPost("interactions/delete")]
	public async Task<IActionResult> DeleteInteractions([FromBody] AdminDeleteInteractionsRequest request, CancellationToken cancellationToken)
	{
		var ids = request.Ids
			.Where(x => x > 0)
			.Distinct()
			.Take(5000)
			.ToArray();

		if (ids.Length == 0)
		{
			return BadRequest("Ids is required.");
		}

		await db.Interactions
			.Where(x => ids.Contains(x.Id))
			.ExecuteDeleteAsync(cancellationToken);

		return NoContent();
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

		if (v.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
		{
			throw new ArgumentException("UserId must contain only letters, digits, hyphens, and underscores.");
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

