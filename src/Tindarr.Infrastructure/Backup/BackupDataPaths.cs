namespace Tindarr.Infrastructure.Backup;

/// <summary>Resolved paths for all databases and tmdb-images used by backup/restore.</summary>
public sealed record BackupDataPaths(
	string DataDir,
	string MainDbPath,
	string PlexCacheDbPath,
	string JellyfinCacheDbPath,
	string EmbyCacheDbPath,
	string TmdbMetadataDbPath,
	string TmdbImagesDir);
