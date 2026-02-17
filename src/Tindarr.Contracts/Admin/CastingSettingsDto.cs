namespace Tindarr.Contracts.Admin;

public sealed record CastingSettingsDto(
	string? PreferredSubtitleSource,
	string? PreferredSubtitleLanguage,
	string? PreferredSubtitleTrackSource,
	string? SubtitleFallback,
	string? SubtitleLanguageFallback,
	string? SubtitleTrackSourceFallback,
	string? PreferredAudioStyle,
	string? PreferredAudioLanguage,
	string? PreferredAudioTrackKind,
	string? AudioFallback,
	string? AudioLanguageFallback,
	string? AudioTrackKindFallback,
	string UpdatedAtUtc);
