namespace Tindarr.Contracts.Emby;

public sealed record EmbyLibrarySyncResponse(
	string ServiceType,
	string ServerId,
	int Count,
	DateTimeOffset SyncedAtUtc);
