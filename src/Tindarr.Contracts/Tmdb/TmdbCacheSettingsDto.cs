namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbCacheSettingsDto(
	int MaxRows,
	int CurrentRows,
	int MaxMovies,
	int CurrentMovies,
	int ImageCacheMaxMb,
	long ImageCacheBytes,
	string PosterMode);
