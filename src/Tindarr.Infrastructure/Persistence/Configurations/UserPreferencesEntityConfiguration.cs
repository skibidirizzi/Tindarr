using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class UserPreferencesEntityConfiguration : IEntityTypeConfiguration<UserPreferencesEntity>
{
	public void Configure(EntityTypeBuilder<UserPreferencesEntity> builder)
	{
		builder.ToTable("user_preferences");

		builder.HasKey(x => x.UserId);
		builder.Property(x => x.UserId).IsRequired();

		builder.Property(x => x.IncludeAdult).IsRequired();
		builder.Property(x => x.MinReleaseYear);
		builder.Property(x => x.MaxReleaseYear);

		builder.Property(x => x.PreferredGenresJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.PreferredOriginalLanguagesJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.UpdatedAtUtc).IsRequired();
	}
}

