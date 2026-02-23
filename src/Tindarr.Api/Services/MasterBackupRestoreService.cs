using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tindarr.Infrastructure.Backup;
using Tindarr.Infrastructure.Persistence;

namespace Tindarr.Api.Services;

public sealed class MasterBackupRestoreService(BackupDataPaths paths, IServiceScopeFactory scopeFactory)
{
	private const string ZipTindarrDb = "tindarr.db";
	private const string ZipPlexCacheDb = "plexcache.db";
	private const string ZipJellyfinCacheDb = "jellyfincache.db";
	private const string ZipEmbyCacheDb = "embycache.db";
	private const string ZipTmdbMetadataDb = "tmdbmetadata.db";
	private const string ZipTmdbImagesFolder = "tmdb-images/";

	/// <summary>Writes a zip containing all DBs and optionally tmdb-images to <paramref name="outputStream"/>.</summary>
	public async Task WriteMasterBackupZipAsync(Stream outputStream, bool includeTmdbImages, CancellationToken cancellationToken = default)
	{
		using var zip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);

		await AddMainDbToZipAsync(zip, cancellationToken).ConfigureAwait(false);
		await AddDbToZipIfExists(zip, paths.PlexCacheDbPath, ZipPlexCacheDb, cancellationToken).ConfigureAwait(false);
		await AddDbToZipIfExists(zip, paths.JellyfinCacheDbPath, ZipJellyfinCacheDb, cancellationToken).ConfigureAwait(false);
		await AddDbToZipIfExists(zip, paths.EmbyCacheDbPath, ZipEmbyCacheDb, cancellationToken).ConfigureAwait(false);
		await AddDbToZipIfExists(zip, paths.TmdbMetadataDbPath, ZipTmdbMetadataDb, cancellationToken).ConfigureAwait(false);

		if (includeTmdbImages && Directory.Exists(paths.TmdbImagesDir))
		{
			var files = Directory.GetFiles(paths.TmdbImagesDir, "*", SearchOption.AllDirectories);
			foreach (var fullPath in files)
			{
				var relativePath = Path.GetRelativePath(paths.TmdbImagesDir, fullPath).Replace('\\', '/');
				var entryName = ZipTmdbImagesFolder + relativePath;
				var entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
				entry.LastWriteTime = File.GetLastWriteTimeUtc(fullPath);
				await using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				await using (var entryStream = entry.Open())
				{
					await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
				}
			}
		}
	}

	/// <summary>Backs up the main app DB using SQLite's backup API so the file can stay in use by the process.</summary>
	private async Task AddMainDbToZipAsync(ZipArchive zip, CancellationToken cancellationToken)
	{
		if (!File.Exists(paths.MainDbPath))
			return;

		var tempPath = Path.Combine(Path.GetTempPath(), $"tindarr-backup-{Guid.NewGuid():N}.db");
		try
		{
			using (var scope = scopeFactory.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<TindarrDbContext>();
				await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
				var connection = db.Database.GetDbConnection();
				if (connection is not SqliteConnection sqliteConnection)
				{
					// Fallback: copy file (may fail if DB is locked)
					await AddDbToZipIfExists(zip, paths.MainDbPath, ZipTindarrDb, cancellationToken).ConfigureAwait(false);
					return;
				}

				await using (var destConn = new SqliteConnection($"Data Source={tempPath};Mode=ReadWriteCreate;"))
				{
					await destConn.OpenAsync(cancellationToken).ConfigureAwait(false);
					sqliteConnection.BackupDatabase(destConn);
					await destConn.CloseAsync().ConfigureAwait(false);
				}
			}

			// On Windows the destination connection may not release the file handle immediately; use ReadWrite share and retry.
			const int maxAttempts = 5;
			const int delayMs = 50;
			for (var attempt = 0; attempt < maxAttempts; attempt++)
			{
				try
				{
					await using (var dbStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
					{
						var entry = zip.CreateEntry(ZipTindarrDb, CompressionLevel.SmallestSize);
						entry.LastWriteTime = DateTimeOffset.UtcNow.DateTime;
						await using (var entryStream = entry.Open())
							await dbStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
					}
					break;
				}
				catch (IOException) when (attempt < maxAttempts - 1)
				{
					await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
				}
			}
		}
		finally
		{
			try
			{
				if (File.Exists(tempPath))
					File.Delete(tempPath);
			}
			catch
			{
				// best-effort cleanup
			}
		}
	}

	private static async Task AddDbToZipIfExists(ZipArchive zip, string dbPath, string entryName, CancellationToken cancellationToken)
	{
		if (!File.Exists(dbPath))
			return;
		// FileShare.ReadWrite allows the DB to remain open by other connections (e.g. SQLite) while we read.
		using var dbStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		var entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
		entry.LastWriteTime = DateTimeOffset.UtcNow.DateTime;
		await using (var entryStream = entry.Open())
			await dbStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
	}
}
