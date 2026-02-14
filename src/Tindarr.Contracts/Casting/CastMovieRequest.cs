namespace Tindarr.Contracts.Casting;

public sealed record CastMovieRequest(
	string DeviceId,
	string ServiceType,
	string ServerId,
	int TmdbId,
	string? Title);
