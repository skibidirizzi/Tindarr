namespace Tindarr.Contracts.Plex;

public sealed record PlexLibrarySyncStatusDto(
	string ServiceType,
	string ServerId,
	string State,
	int TotalSections,
	int ProcessedSections,
	int TotalItems,
	int ProcessedItems,
	int TmdbIdsFound,
	DateTimeOffset? StartedAtUtc,
	DateTimeOffset? FinishedAtUtc,
	string? Message,
	DateTimeOffset UpdatedAtUtc);
