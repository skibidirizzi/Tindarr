using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class ServiceSettingsEntity
{
	public long Id { get; set; }

	public ServiceType ServiceType { get; set; }
	public string ServerId { get; set; } = "";

	public string RadarrBaseUrl { get; set; } = "";
	public string RadarrApiKey { get; set; } = "";
	public int? RadarrQualityProfileId { get; set; }
	public string? RadarrRootFolderPath { get; set; }
	public string? RadarrTagLabel { get; set; }
	public int? RadarrTagId { get; set; }
	public bool RadarrAutoAddEnabled { get; set; }
	public long? RadarrLastAutoAddAcceptedId { get; set; }
	public DateTimeOffset? RadarrLastLibrarySyncUtc { get; set; }

	public string? PlexClientIdentifier { get; set; }
	public string? PlexAuthToken { get; set; }
	public string? PlexServerName { get; set; }
	public string? PlexServerUri { get; set; }
	public string? PlexServerVersion { get; set; }
	public string? PlexServerPlatform { get; set; }
	public bool? PlexServerOwned { get; set; }
	public bool? PlexServerOnline { get; set; }
	public string? PlexServerAccessToken { get; set; }
	public DateTimeOffset? PlexLastLibrarySyncUtc { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
