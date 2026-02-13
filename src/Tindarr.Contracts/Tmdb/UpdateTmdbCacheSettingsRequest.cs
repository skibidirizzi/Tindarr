namespace Tindarr.Contracts.Tmdb;

public sealed record UpdateTmdbCacheSettingsRequest(
	int MaxRows,
	int MaxMovies,
	int ImageCacheMaxMb,
	string PosterMode);
