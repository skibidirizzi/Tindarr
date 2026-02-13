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
		builder.Property(x => x.MinRating);
		builder.Property(x => x.MaxRating);

		builder.Property(x => x.PreferredGenresJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.ExcludedGenresJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.PreferredOriginalLanguagesJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.ExcludedOriginalLanguagesJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.PreferredRegionsJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.ExcludedRegionsJson)
			.IsRequired()
			.HasDefaultValue("[]");

		builder.Property(x => x.SortBy)
			.IsRequired()
			.HasMaxLength(64)
			.HasDefaultValue("popularity.desc");

		builder.Property(x => x.UpdatedAtUtc).IsRequired();
	}
}

