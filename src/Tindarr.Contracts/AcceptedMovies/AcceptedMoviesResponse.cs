namespace Tindarr.Contracts.AcceptedMovies;

public sealed record AcceptedMoviesResponse(
	string ServiceType,
	string ServerId,
	IReadOnlyList<AcceptedMovieDto> Items);

