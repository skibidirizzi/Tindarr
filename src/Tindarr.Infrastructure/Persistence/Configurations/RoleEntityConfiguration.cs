using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class RoleEntityConfiguration : IEntityTypeConfiguration<RoleEntity>
{
	public void Configure(EntityTypeBuilder<RoleEntity> builder)
	{
		builder.ToTable("roles");

		builder.HasKey(x => x.Name);
		builder.Property(x => x.Name)
			.IsRequired()
			.HasMaxLength(32);

		builder.Property(x => x.CreatedAtUtc).IsRequired();

		builder.HasMany(x => x.UserRoles)
			.WithOne(x => x.Role)
			.HasForeignKey(x => x.RoleName)
			.OnDelete(DeleteBehavior.Cascade);

		// Seed baseline roles (matches Tindarr.Api.Auth.Policies role names).
		var seedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		builder.HasData(
			new RoleEntity { Name = "Admin", CreatedAtUtc = seedAt },
			new RoleEntity { Name = "Curator", CreatedAtUtc = seedAt },
			new RoleEntity { Name = "Contributor", CreatedAtUtc = seedAt }
		);
	}
}

