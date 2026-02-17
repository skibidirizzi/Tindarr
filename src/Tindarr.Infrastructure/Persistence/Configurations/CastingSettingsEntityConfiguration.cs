using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class CastingSettingsEntityConfiguration : IEntityTypeConfiguration<CastingSettingsEntity>
{
	public void Configure(EntityTypeBuilder<CastingSettingsEntity> builder)
	{
		builder.ToTable("casting_settings");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).ValueGeneratedOnAdd();

		builder.Property(x => x.PreferredSubtitleSource);
		builder.Property(x => x.PreferredSubtitleLanguage);
		builder.Property(x => x.PreferredSubtitleTrackSource);
		builder.Property(x => x.SubtitleFallback);
		builder.Property(x => x.SubtitleLanguageFallback);
		builder.Property(x => x.SubtitleTrackSourceFallback);
		builder.Property(x => x.PreferredAudioStyle);
		builder.Property(x => x.PreferredAudioLanguage);
		builder.Property(x => x.PreferredAudioTrackKind);
		builder.Property(x => x.AudioFallback);
		builder.Property(x => x.AudioLanguageFallback);
		builder.Property(x => x.AudioTrackKindFallback);
		builder.Property(x => x.UpdatedAtUtc).IsRequired();
	}
}
