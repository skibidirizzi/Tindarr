namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbImportResultDto(
	int Inserted,
	int Updated,
	int Skipped,
	IReadOnlyList<string> NotImportedReasons);
