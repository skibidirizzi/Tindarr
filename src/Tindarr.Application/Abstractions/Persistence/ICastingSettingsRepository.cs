namespace Tindarr.Application.Abstractions.Persistence;

public interface ICastingSettingsRepository
{
	Task<CastingSettingsRecord?> GetAsync(CancellationToken cancellationToken);

	Task<CastingSettingsRecord> UpsertAsync(CastingSettingsUpsert upsert, CancellationToken cancellationToken);
}

public sealed record CastingSettingsRecord(
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
	DateTimeOffset UpdatedAtUtc);

public sealed record CastingSettingsUpsert(
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
	string? AudioTrackKindFallback);
