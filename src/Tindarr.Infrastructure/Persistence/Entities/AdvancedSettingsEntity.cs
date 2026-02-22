namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class AdvancedSettingsEntity
{
	public long Id { get; set; }
	public bool? ApiRateLimitEnabled { get; set; }
	public int? ApiRateLimitPermitLimit { get; set; }
	public int? ApiRateLimitWindowMinutes { get; set; }
	public bool? CleanupEnabled { get; set; }
	public int? CleanupIntervalMinutes { get; set; }
	public bool? CleanupPurgeGuestUsers { get; set; }
	public int? CleanupGuestUserMaxAgeHours { get; set; }
	/// <summary>TMDB API key (v3). When set, overrides Tmdb:ApiKey / TMDB_API_KEY. Never returned to clients.</summary>
	public string? TmdbApiKey { get; set; }
	/// <summary>TMDB Read Access Token (Bearer). When set, overrides Tmdb:ReadAccessToken. Never returned to clients.</summary>
	public string? TmdbReadAccessToken { get; set; }
	/// <summary>How to display date/time in the UI: locale, 12h, 24h, relative. Null = use default (locale).</summary>
	public string? DateTimeDisplayMode { get; set; }
	/// <summary>Time zone for display: Local, UTC, or IANA id (e.g. America/New_York). Null = Local.</summary>
	public string? TimeZoneId { get; set; }
	/// <summary>Date order: locale, mdy, dmy, ymd. Null = locale.</summary>
	public string? DateOrder { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
