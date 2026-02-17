namespace Tindarr.Contracts.Playback;

public sealed record PrepareMoviePlaybackRequest(
	string ServiceType,
	string ServerId,
	int TmdbId);
