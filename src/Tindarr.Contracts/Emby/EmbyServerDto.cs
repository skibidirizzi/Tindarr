namespace Tindarr.Contracts.Emby;

public sealed record EmbyServerDto(
	string ServerId,
	string Name,
	string? BaseUrl,
	string? Version,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);
