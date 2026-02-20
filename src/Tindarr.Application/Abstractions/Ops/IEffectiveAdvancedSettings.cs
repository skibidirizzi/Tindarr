using Tindarr.Application.Options;

namespace Tindarr.Application.Abstractions.Ops;

/// <summary>
/// Provides effective advanced settings (DB overrides merged with config defaults).
/// Cache is invalidated when admin updates settings so rate limiter and cleanup use new values.
/// </summary>
public interface IEffectiveAdvancedSettings
{
	ApiRateLimitOptions GetApiRateLimitOptions();

	CleanupOptions GetCleanupOptions();

	/// <summary>
	/// TMDB API key: DB value if set and non-empty, otherwise Tmdb:ApiKey / TMDB_API_KEY.
	/// </summary>
	string GetEffectiveTmdbApiKey();

	/// <summary>
	/// True if TMDB is configured (effective API key or Read Access Token present).
	/// </summary>
	bool HasEffectiveTmdbCredentials();

	/// <summary>
	/// How to display date/time in the UI: locale, 12h, 24h, relative. Default is locale.
	/// </summary>
	string GetDateTimeDisplayMode();

	/// <summary>
	/// Time zone for display: Local, UTC, or IANA id (e.g. America/New_York). Default is Local.
	/// </summary>
	string GetTimeZoneId();

	/// <summary>
	/// Date order: locale, mdy, dmy, ymd. Default is locale.
	/// </summary>
	string GetDateOrder();

	/// <summary>
	/// Call after updating advanced settings so next get uses fresh values.
	/// </summary>
	void Invalidate();
}
