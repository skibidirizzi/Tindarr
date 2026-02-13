namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbFetchMovieImagesResultDto(
	int TmdbId,
	bool PosterFetched,
	bool BackdropFetched,
	string? Message);
