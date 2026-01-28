namespace Tindarr.Contracts.Radarr;

public sealed record RadarrLibrarySyncResponse(string ServiceType, string ServerId, int Count, DateTimeOffset SyncedAtUtc);
