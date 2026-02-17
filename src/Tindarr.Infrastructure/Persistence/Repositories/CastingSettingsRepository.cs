using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class CastingSettingsRepository(TindarrDbContext db) : ICastingSettingsRepository
{
	public async Task<CastingSettingsRecord?> GetAsync(CancellationToken cancellationToken)
	{
		var entity = await db.CastingSettings
			.AsNoTracking()
			.SingleOrDefaultAsync(cancellationToken);

		return entity is null ? null : Map(entity);
	}

	public async Task<CastingSettingsRecord> UpsertAsync(CastingSettingsUpsert upsert, CancellationToken cancellationToken)
	{
		var entity = await db.CastingSettings
			.SingleOrDefaultAsync(cancellationToken);

		var now = DateTimeOffset.UtcNow;
		if (entity is null)
		{
			entity = new CastingSettingsEntity
			{
				PreferredSubtitleSource = upsert.PreferredSubtitleSource,
				PreferredSubtitleLanguage = upsert.PreferredSubtitleLanguage,
				PreferredSubtitleTrackSource = upsert.PreferredSubtitleTrackSource,
				SubtitleFallback = upsert.SubtitleFallback,
				SubtitleLanguageFallback = upsert.SubtitleLanguageFallback,
				SubtitleTrackSourceFallback = upsert.SubtitleTrackSourceFallback,
				PreferredAudioStyle = upsert.PreferredAudioStyle,
				PreferredAudioLanguage = upsert.PreferredAudioLanguage,
				PreferredAudioTrackKind = upsert.PreferredAudioTrackKind,
				AudioFallback = upsert.AudioFallback,
				AudioLanguageFallback = upsert.AudioLanguageFallback,
				AudioTrackKindFallback = upsert.AudioTrackKindFallback,
				UpdatedAtUtc = now
			};
			db.CastingSettings.Add(entity);
		}
		else
		{
			entity.PreferredSubtitleSource = upsert.PreferredSubtitleSource;
			entity.PreferredSubtitleLanguage = upsert.PreferredSubtitleLanguage;
			entity.PreferredSubtitleTrackSource = upsert.PreferredSubtitleTrackSource;
			entity.SubtitleFallback = upsert.SubtitleFallback;
			entity.SubtitleLanguageFallback = upsert.SubtitleLanguageFallback;
			entity.SubtitleTrackSourceFallback = upsert.SubtitleTrackSourceFallback;
			entity.PreferredAudioStyle = upsert.PreferredAudioStyle;
			entity.PreferredAudioLanguage = upsert.PreferredAudioLanguage;
			entity.PreferredAudioTrackKind = upsert.PreferredAudioTrackKind;
			entity.AudioFallback = upsert.AudioFallback;
			entity.AudioLanguageFallback = upsert.AudioLanguageFallback;
			entity.AudioTrackKindFallback = upsert.AudioTrackKindFallback;
			entity.UpdatedAtUtc = now;
		}

		await db.SaveChangesAsync(cancellationToken);
		return Map(entity);
	}

	private static CastingSettingsRecord Map(CastingSettingsEntity entity)
	{
		return new CastingSettingsRecord(
			entity.PreferredSubtitleSource,
			entity.PreferredSubtitleLanguage,
			entity.PreferredSubtitleTrackSource,
			entity.SubtitleFallback,
			entity.SubtitleLanguageFallback,
			entity.SubtitleTrackSourceFallback,
			entity.PreferredAudioStyle,
			entity.PreferredAudioLanguage,
			entity.PreferredAudioTrackKind,
			entity.AudioFallback,
			entity.AudioLanguageFallback,
			entity.AudioTrackKindFallback,
			entity.UpdatedAtUtc);
	}
}
