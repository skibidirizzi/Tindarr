namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbStoredMovieAdminListResponse(
	IReadOnlyList<TmdbStoredMovieAdminDto> Items,
	int Skip,
	int Take,
	int NextSkip,
	bool HasMore);
