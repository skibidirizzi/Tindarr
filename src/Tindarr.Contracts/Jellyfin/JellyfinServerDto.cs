namespace Tindarr.Contracts.Jellyfin;

public sealed record JellyfinServerDto(
	string ServerId,
	string Name,
	string? BaseUrl,
	string? Version,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);
