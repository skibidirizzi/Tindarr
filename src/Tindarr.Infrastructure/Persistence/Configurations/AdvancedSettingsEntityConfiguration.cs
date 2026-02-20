using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class AdvancedSettingsEntityConfiguration : IEntityTypeConfiguration<AdvancedSettingsEntity>
{
	public void Configure(EntityTypeBuilder<AdvancedSettingsEntity> builder)
	{
		builder.ToTable("AdvancedSettings");
		builder.HasKey(x => x.Id);
	}
}
