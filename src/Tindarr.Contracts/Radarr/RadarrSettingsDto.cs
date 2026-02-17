namespace Tindarr.Contracts.Radarr;

public sealed record RadarrSettingsDto(
	string ServiceType,
	string ServerId,
	bool Configured,
	string? BaseUrl,
	int? QualityProfileId,
	string? RootFolderPath,
	string? TagLabel,
	bool AutoAddEnabled,
	int? AutoAddIntervalMinutes,
	bool HasApiKey,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset? UpdatedAtUtc);
