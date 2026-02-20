namespace Tindarr.Contracts.Admin;

/// <summary>
/// Request to update advanced settings. Null values mean "use server default" (config).
/// When TmdbApiKeySet is true, TmdbApiKey is applied (non-empty = store in DB, null/empty = clear DB override).
/// When TmdbApiKeySet is false or omitted, TmdbApiKey is left unchanged.
/// </summary>
public sealed record UpdateAdvancedSettingsRequest(
	bool? ApiRateLimitEnabled,
	int? ApiRateLimitPermitLimit,
	int? ApiRateLimitWindowMinutes,
	bool? CleanupEnabled,
	int? CleanupIntervalMinutes,
	bool? CleanupPurgeGuestUsers,
	int? CleanupGuestUserMaxAgeHours,
	bool? TmdbApiKeySet,
	string? TmdbApiKey,
	string? DateTimeDisplayMode,
	string? TimeZoneId,
	string? DateOrder);
