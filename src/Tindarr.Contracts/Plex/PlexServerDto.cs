namespace Tindarr.Contracts.Plex;

public sealed record PlexServerDto(
	string ServerId,
	string Name,
	string? BaseUrl,
	string? Version,
	string? Platform,
	bool? Owned,
	bool? Online,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);
