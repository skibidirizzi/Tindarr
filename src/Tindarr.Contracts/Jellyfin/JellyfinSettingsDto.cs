namespace Tindarr.Contracts.Jellyfin;

public sealed record JellyfinSettingsDto(
	string ServiceType,
	string ServerId,
	bool Configured,
	string? BaseUrl,
	bool HasApiKey,
	string? ServerName,
	string? ServerVersion,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset? UpdatedAtUtc);
