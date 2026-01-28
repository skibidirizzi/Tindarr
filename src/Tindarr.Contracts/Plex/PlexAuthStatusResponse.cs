namespace Tindarr.Contracts.Plex;

public sealed record PlexAuthStatusResponse(
	bool HasClientIdentifier,
	bool HasAuthToken);
