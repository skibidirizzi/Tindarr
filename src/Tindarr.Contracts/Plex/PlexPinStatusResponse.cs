namespace Tindarr.Contracts.Plex;

public sealed record PlexPinStatusResponse(
	long PinId,
	string Code,
	DateTimeOffset? ExpiresAtUtc,
	bool Authorized);
