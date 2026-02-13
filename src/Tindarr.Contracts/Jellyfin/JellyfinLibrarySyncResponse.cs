namespace Tindarr.Contracts.Jellyfin;

public sealed record JellyfinLibrarySyncResponse(
	string ServiceType,
	string ServerId,
	int Count,
	DateTimeOffset SyncedAtUtc);
