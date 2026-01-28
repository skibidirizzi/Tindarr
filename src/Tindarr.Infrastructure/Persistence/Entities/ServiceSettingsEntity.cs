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
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
