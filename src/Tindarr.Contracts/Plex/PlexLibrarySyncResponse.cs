namespace Tindarr.Contracts.Plex;

public sealed record PlexLibrarySyncResponse(
	string ServiceType,
	string ServerId,
	int Count,
	DateTimeOffset SyncedAtUtc);
