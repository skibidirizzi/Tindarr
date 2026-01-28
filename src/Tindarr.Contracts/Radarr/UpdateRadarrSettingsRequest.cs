namespace Tindarr.Contracts.Radarr;

public sealed record UpdateRadarrSettingsRequest(
	string BaseUrl,
	string? ApiKey,
	int? QualityProfileId,
	string? RootFolderPath,
	string? TagLabel,
	bool AutoAddEnabled);
