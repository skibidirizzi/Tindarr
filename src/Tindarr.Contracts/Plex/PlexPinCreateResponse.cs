namespace Tindarr.Contracts.Plex;

public sealed record PlexPinCreateResponse(
	long PinId,
	string Code,
	DateTimeOffset? ExpiresAtUtc,
	string AuthUrl);
