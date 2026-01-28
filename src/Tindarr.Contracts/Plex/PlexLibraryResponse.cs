using Tindarr.Contracts.Movies;

namespace Tindarr.Contracts.Plex;

public sealed record PlexLibraryResponse(
	string ServiceType,
	string ServerId,
	int Count,
	IReadOnlyList<MovieDetailsDto> Items);
