namespace Tindarr.Contracts.Jellyfin;

public sealed record UpdateJellyfinSettingsRequest(
	string BaseUrl,
	string ApiKey);
