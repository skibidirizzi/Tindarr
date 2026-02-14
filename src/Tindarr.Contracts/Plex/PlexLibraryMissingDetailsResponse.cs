namespace Tindarr.Contracts.Plex;

public sealed record PlexLibraryMissingDetailsResponse(
	string ServiceType,
	string ServerId,
	int Count,
	IReadOnlyList<PlexLibraryMissingDetailsItemDto> Items);
