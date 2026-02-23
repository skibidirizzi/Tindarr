using Microsoft.Extensions.Configuration;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Backup;

/// <summary>Resolves backup data paths using the same logic as persistence (data dir + file names).</summary>
public static class BackupPathResolver
{
	/// <summary>Resolve all paths used for backup/restore. <paramref name="overrideDataDir"/> must match the value passed to AddTindarrPersistence/AddPlexCache/etc.</summary>
	public static BackupDataPaths Resolve(IConfiguration configuration, string? overrideDataDir, string tmdbMetadataDbPath)
	{
		var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
		var dataDir = ResolveDataDir(dbOptions, overrideDataDir);
		var mainDbPath = ResolveSqlitePath(dbOptions, dataDir);
		var tmdbImagesDir = Path.Combine(Path.GetDirectoryName(tmdbMetadataDbPath) ?? dataDir, "tmdb-images");
		return new BackupDataPaths(
			DataDir: dataDir,
			MainDbPath: mainDbPath,
			PlexCacheDbPath: Path.Combine(dataDir, "plexcache.db"),
			JellyfinCacheDbPath: Path.Combine(dataDir, "jellyfincache.db"),
			EmbyCacheDbPath: Path.Combine(dataDir, "embycache.db"),
			TmdbMetadataDbPath: tmdbMetadataDbPath,
			TmdbImagesDir: tmdbImagesDir);
	}

	private static string ResolveDataDir(DatabaseOptions options, string? overrideDataDir)
	{
		if (!string.IsNullOrWhiteSpace(overrideDataDir))
			return overrideDataDir;
		if (!string.IsNullOrWhiteSpace(options.DataDir))
			return options.DataDir;
		return AppContext.BaseDirectory;
	}

	private static string ResolveSqlitePath(DatabaseOptions options, string dataDir)
	{
		if (Path.IsPathRooted(options.SqliteFileName))
			return options.SqliteFileName;
		return Path.Combine(dataDir, options.SqliteFileName);
	}
}
