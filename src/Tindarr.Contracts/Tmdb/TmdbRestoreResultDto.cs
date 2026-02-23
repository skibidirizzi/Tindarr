namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbRestoreResultDto(
	int Inserted,
	int Updated,
	int Skipped,
	int ImagesRestored,
	IReadOnlyList<string> NotImportedReasons);
