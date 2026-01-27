using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
	public void Configure(EntityTypeBuilder<UserEntity> builder)
	{
		builder.ToTable("users");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).IsRequired();

		builder.Property(x => x.DisplayName)
			.IsRequired()
			.HasMaxLength(64);

		builder.Property(x => x.CreatedAtUtc)
			.IsRequired();

		builder.Property(x => x.PasswordHash);
		builder.Property(x => x.PasswordSalt);
		builder.Property(x => x.PasswordIterations);

		builder.HasMany(x => x.UserRoles)
			.WithOne(x => x.User)
			.HasForeignKey(x => x.UserId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.HasOne(x => x.Preferences)
			.WithOne(x => x.User)
			.HasForeignKey<UserPreferencesEntity>(x => x.UserId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}

