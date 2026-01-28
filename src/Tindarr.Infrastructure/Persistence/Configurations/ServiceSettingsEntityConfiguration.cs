using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class ServiceSettingsEntityConfiguration : IEntityTypeConfiguration<ServiceSettingsEntity>
{
	public void Configure(EntityTypeBuilder<ServiceSettingsEntity> builder)
	{
		builder.ToTable("service_settings");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).ValueGeneratedOnAdd();

		builder.Property(x => x.ServiceType).IsRequired();
		builder.Property(x => x.ServerId).IsRequired();

		builder.Property(x => x.RadarrBaseUrl).IsRequired();
		builder.Property(x => x.RadarrApiKey).IsRequired();
		builder.Property(x => x.RadarrQualityProfileId);
		builder.Property(x => x.RadarrRootFolderPath);
		builder.Property(x => x.RadarrTagLabel);
		builder.Property(x => x.RadarrTagId);
		builder.Property(x => x.RadarrAutoAddEnabled).IsRequired();
		builder.Property(x => x.RadarrLastAutoAddAcceptedId);
		builder.Property(x => x.RadarrLastLibrarySyncUtc);
		builder.Property(x => x.UpdatedAtUtc).IsRequired();

		builder.HasIndex(x => new { x.ServiceType, x.ServerId }).IsUnique();
	}
}
