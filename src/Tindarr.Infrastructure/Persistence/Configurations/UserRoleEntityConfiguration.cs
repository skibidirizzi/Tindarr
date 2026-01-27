using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class UserRoleEntityConfiguration : IEntityTypeConfiguration<UserRoleEntity>
{
	public void Configure(EntityTypeBuilder<UserRoleEntity> builder)
	{
		builder.ToTable("user_roles");

		builder.HasKey(x => new { x.UserId, x.RoleName });

		builder.Property(x => x.UserId).IsRequired();
		builder.Property(x => x.RoleName).IsRequired();

		builder.HasIndex(x => x.RoleName);
	}
}

