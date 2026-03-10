namespace Tindarr.Contracts.Admin;

/// <summary>
/// Current advanced settings and their built-in defaults (for UI comparison and warnings).
/// TMDB API key is never returned; only whether one is configured (HasTmdbApiKey).
/// </summary>
public sealed record AdvancedSettingsDto(
	AdvancedSettingsApiRateLimitDto ApiRateLimit,
	AdvancedSettingsApiRateLimitDto ApiRateLimitDefaults,
	AdvancedSettingsCleanupDto Cleanup,
	AdvancedSettingsCleanupDto CleanupDefaults,
	AdvancedSettingsNotificationsDto Notifications,
	AdvancedSettingsNotificationsDto NotificationsDefaults,
	AdvancedSettingsTmdbDto Tmdb,
	AdvancedSettingsDisplayDto Display,
	AdvancedSettingsDisplayDto DisplayDefaults);

public sealed record AdvancedSettingsNotificationsDto(
	bool Enabled,
	IReadOnlyList<string> WebhookUrls,
	AdvancedSettingsNotificationsEventsDto Events);

public sealed record AdvancedSettingsNotificationsEventsDto(
	bool Likes,
	bool Matches,
	bool RoomCreated,
	bool Login,
	bool UserCreated,
	bool AuthFailures);

/// <summary>
/// UI display preferences (e.g. date/time format, time zone, date order). Readable by any authenticated user via settings/display.
/// </summary>
public sealed record AdvancedSettingsDisplayDto(
	string DateTimeDisplayMode,
	string TimeZoneId,
	string DateOrder);

/// <summary>
/// TMDB-related advanced settings. Key and token are never exposed (INV-0006).
/// </summary>
public sealed record AdvancedSettingsTmdbDto(bool HasTmdbApiKey, bool HasTmdbReadAccessToken);

public sealed record AdvancedSettingsApiRateLimitDto(
	bool Enabled,
	int PermitLimit,
	int WindowMinutes);

public sealed record AdvancedSettingsCleanupDto(
	bool Enabled,
	int IntervalMinutes,
	bool PurgeGuestUsers,
	int GuestUserMaxAgeHours);
