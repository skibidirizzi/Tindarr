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
	AdvancedSettingsTmdbDto Tmdb,
	AdvancedSettingsDisplayDto Display,
	AdvancedSettingsDisplayDto DisplayDefaults);

/// <summary>
/// UI display preferences (e.g. date/time format, time zone, date order). Readable by any authenticated user via settings/display.
/// </summary>
public sealed record AdvancedSettingsDisplayDto(
	string DateTimeDisplayMode,
	string TimeZoneId,
	string DateOrder);

/// <summary>
/// TMDB-related advanced settings. Key value is never exposed (INV-0006).
/// </summary>
public sealed record AdvancedSettingsTmdbDto(bool HasTmdbApiKey);

public sealed record AdvancedSettingsApiRateLimitDto(
	bool Enabled,
	int PermitLimit,
	int WindowMinutes);

public sealed record AdvancedSettingsCleanupDto(
	bool Enabled,
	int IntervalMinutes,
	bool PurgeGuestUsers,
	int GuestUserMaxAgeHours);
