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

		builder.Property(x => x.JellyfinBaseUrl);
		builder.Property(x => x.JellyfinApiKey);
		builder.Property(x => x.JellyfinServerName);
		builder.Property(x => x.JellyfinServerVersion);
		builder.Property(x => x.JellyfinLastLibrarySyncUtc);

		builder.Property(x => x.EmbyBaseUrl);
		builder.Property(x => x.EmbyApiKey);
		builder.Property(x => x.EmbyServerName);
		builder.Property(x => x.EmbyServerVersion);
		builder.Property(x => x.EmbyLastLibrarySyncUtc);

		builder.Property(x => x.PlexClientIdentifier);
		builder.Property(x => x.PlexAuthToken);
		builder.Property(x => x.PlexServerName);
		builder.Property(x => x.PlexServerUri);
		builder.Property(x => x.PlexServerVersion);
		builder.Property(x => x.PlexServerPlatform);
		builder.Property(x => x.PlexServerOwned);
		builder.Property(x => x.PlexServerOnline);
		builder.Property(x => x.PlexServerAccessToken);
		builder.Property(x => x.PlexLastLibrarySyncUtc);
		builder.Property(x => x.UpdatedAtUtc).IsRequired();

		builder.HasIndex(x => new { x.ServiceType, x.ServerId }).IsUnique();
	}
}
