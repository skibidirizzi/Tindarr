namespace Tindarr.Contracts.AcceptedMovies;

public sealed record AcceptedMovieDto(
	int TmdbId,
	string? AcceptedByUserId,
	DateTimeOffset AcceptedAtUtc);

