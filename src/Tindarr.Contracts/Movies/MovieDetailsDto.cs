namespace Tindarr.Contracts.Movies;

public sealed record MovieDetailsDto(
	int TmdbId,
	string Title,
	string? Overview,
	string? PosterUrl,
	string? BackdropUrl,
	string? ReleaseDate,
	int? ReleaseYear,
	string? MpaaRating,
	double? Rating,
	int? VoteCount,
	IReadOnlyList<string> Genres,
	string? OriginalLanguage,
	int? RuntimeMinutes);

