namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class CastingSettingsEntity
{
	public long Id { get; set; }

	public string? PreferredSubtitleSource { get; set; }
	public string? PreferredSubtitleLanguage { get; set; }
	public string? PreferredSubtitleTrackSource { get; set; }
	public string? SubtitleFallback { get; set; }
	public string? SubtitleLanguageFallback { get; set; }
	public string? SubtitleTrackSourceFallback { get; set; }
	public string? PreferredAudioStyle { get; set; }
	public string? PreferredAudioLanguage { get; set; }
	public string? PreferredAudioTrackKind { get; set; }
	public string? AudioFallback { get; set; }
	public string? AudioLanguageFallback { get; set; }
	public string? AudioTrackKindFallback { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
