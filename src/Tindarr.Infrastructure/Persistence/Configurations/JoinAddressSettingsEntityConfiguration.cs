using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class JoinAddressSettingsEntityConfiguration : IEntityTypeConfiguration<JoinAddressSettingsEntity>
{
	public void Configure(EntityTypeBuilder<JoinAddressSettingsEntity> builder)
	{
		builder.ToTable("JoinAddressSettings");
		builder.HasKey(x => x.Id);
		builder.Property(x => x.LanHostPort).HasMaxLength(255);
		builder.Property(x => x.WanHostPort).HasMaxLength(255);
	}
}
