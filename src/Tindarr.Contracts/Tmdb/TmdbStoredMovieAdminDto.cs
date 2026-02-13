namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbStoredMovieAdminDto(
	int TmdbId,
	string Title,
	int? ReleaseYear,
	string? PosterPath,
	string? BackdropPath,
	DateTimeOffset? DetailsFetchedAtUtc,
	DateTimeOffset? UpdatedAtUtc,
	bool PosterCached,
	bool BackdropCached);
