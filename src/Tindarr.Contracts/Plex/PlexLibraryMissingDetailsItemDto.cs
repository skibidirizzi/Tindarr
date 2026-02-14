namespace Tindarr.Contracts.Plex;

public sealed record PlexLibraryMissingDetailsItemDto(
	int TmdbId,
	string Title);
