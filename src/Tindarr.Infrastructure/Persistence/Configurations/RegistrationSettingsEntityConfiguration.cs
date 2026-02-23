using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class RegistrationSettingsEntityConfiguration : IEntityTypeConfiguration<RegistrationSettingsEntity>
{
	public void Configure(EntityTypeBuilder<RegistrationSettingsEntity> builder)
	{
		builder.ToTable("RegistrationSettings");
		builder.HasKey(x => x.Id);
	}
}
