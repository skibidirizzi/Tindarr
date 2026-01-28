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
	bool HasApiKey,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset? UpdatedAtUtc);
