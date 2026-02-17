namespace Tindarr.Contracts.Admin;

public sealed record UpdateCastingSettingsRequest(
	string? PreferredSubtitleSource,
	string? PreferredSubtitleLanguage,
	string? PreferredSubtitleTrackSource,
	string? SubtitleFallback,
	string? SubtitleLanguageFallback,
	string? SubtitleTrackSourceFallback,
	string? PreferredAudioStyle,
	string? PreferredAudioLanguage,
	string? AudioFallback,
	string? AudioLanguageFallback,
	string? PreferredAudioTrackKind,
	string? AudioTrackKindFallback);
