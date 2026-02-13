namespace Tindarr.Contracts.Emby;

public sealed record UpdateEmbySettingsRequest(
	string BaseUrl,
	string ApiKey);
