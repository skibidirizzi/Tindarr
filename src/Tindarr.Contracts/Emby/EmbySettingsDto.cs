namespace Tindarr.Contracts.Emby;

public sealed record EmbySettingsDto(
	string ServiceType,
	string ServerId,
	bool Configured,
	string? BaseUrl,
	bool HasApiKey,
	string? ServerName,
	string? ServerVersion,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset? UpdatedAtUtc);
