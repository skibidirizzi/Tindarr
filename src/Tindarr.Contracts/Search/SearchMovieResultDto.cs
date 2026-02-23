namespace Tindarr.Contracts.Search;

public sealed record SearchMovieResultDto(
	int TmdbId,
	string Title,
	int? ReleaseYear,
	string? PosterUrl,
	string? BackdropUrl);
