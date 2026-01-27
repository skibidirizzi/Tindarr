namespace Tindarr.Contracts.AcceptedMovies;

public sealed record ForceAcceptMovieRequest(
	int TmdbId,
	string ServiceType,
	string ServerId);

