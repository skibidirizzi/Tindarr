using Tindarr.Contracts.Tmdb;

namespace Tindarr.Contracts.Admin;

public sealed record AdminDbMovieListResponse(
	IReadOnlyList<TmdbStoredMovieAdminDto> Items,
	int Skip,
	int Take,
	int NextSkip,
	bool HasMore,
	int TotalCount);
