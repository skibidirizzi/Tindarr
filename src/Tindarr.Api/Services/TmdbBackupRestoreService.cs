using System.IO.Compression;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Contracts.Tmdb;

namespace Tindarr.Api.Services;

public sealed class TmdbBackupRestoreService(
	string metadataDbPath,
	ITmdbMetadataStore metadataStore)
{
	private const string ZipDbEntryName = "tmdbmetadata.db";
	private const string ZipImagesFolderName = "tmdb-images/";

	private static string ImageDirFromDbPath(string dbPath)
	{
		var dir = Path.GetDirectoryName(dbPath);
		return Path.Combine(string.IsNullOrWhiteSpace(dir) ? AppContext.BaseDirectory : dir, "tmdb-images");
	}

	/// <summary>Returns true if image cache is configured (LocalProxy and max MB > 0).</summary>
	public async Task<bool> GetIncludeImagesAsync(CancellationToken cancellationToken = default)
	{
		var settings = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		return settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0;
	}

	/// <summary>Writes a zip to <paramref name="outputStream"/> containing the metadata DB and optionally the tmdb-images folder.</summary>
	public async Task WriteBackupZipAsync(Stream outputStream, bool includeImages, CancellationToken cancellationToken = default)
	{
		using var zip = new ZipArchive(outputStream, ZipArchiveMode.Create);

		if (File.Exists(metadataDbPath))
		{
			using var dbStream = new FileStream(metadataDbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			var entry = zip.CreateEntry(ZipDbEntryName, CompressionLevel.SmallestSize);
			entry.LastWriteTime = DateTimeOffset.UtcNow.DateTime;
			await using (var entryStream = entry.Open())
			{
				await dbStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
			}
		}

		if (includeImages)
		{
			var imageDir = ImageDirFromDbPath(metadataDbPath);
			if (Directory.Exists(imageDir))
			{
				var files = Directory.GetFiles(imageDir, "*", SearchOption.AllDirectories);
				foreach (var fullPath in files)
				{
					var relativePath = Path.GetRelativePath(imageDir, fullPath).Replace('\\', '/');
					var entryName = ZipImagesFolderName + relativePath;
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

		await Task.CompletedTask.ConfigureAwait(false);
	}

	/// <summary>Restores from a zip: merges any .db/.sqlite into the metadata store and extracts tmdb-images into the image cache dir. Returns merge result + count of images restored.</summary>
	public async Task<TmdbRestoreResultDto> RestoreFromZipAsync(Stream zipStream, CancellationToken cancellationToken = default)
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"tindarr-tmdb-restore-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		try
		{
			using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
			{
				zip.ExtractToDirectory(tempDir, overwriteFiles: true);
			}

			var inserted = 0;
			var updated = 0;
			var skipped = 0;
			var reasons = new List<string>();

			var dbFiles = Directory.GetFiles(tempDir, "*.db", SearchOption.TopDirectoryOnly)
				.Concat(Directory.GetFiles(tempDir, "*.sqlite", SearchOption.TopDirectoryOnly))
				.Concat(Directory.GetFiles(tempDir, "*.sqlite3", SearchOption.TopDirectoryOnly))
				.ToList();

			if (dbFiles.Count > 0)
			{
				var result = await metadataStore.ImportFromFileAsync(dbFiles[0], cancellationToken).ConfigureAwait(false);
				inserted = result.Inserted;
				updated = result.Updated;
				skipped = result.Skipped;
				if (result.NotImportedReasons.Count > 0)
				{
					reasons.AddRange(result.NotImportedReasons);
				}
			}
			else
			{
				reasons.Add("No .db, .sqlite, or .sqlite3 file found in the zip.");
			}

			var imagesRestored = 0;
			var extractedImagesDir = Path.Combine(tempDir, "tmdb-images");
			if (Directory.Exists(extractedImagesDir))
			{
				var imageDir = ImageDirFromDbPath(metadataDbPath);
				Directory.CreateDirectory(imageDir);
				var imageFiles = Directory.GetFiles(extractedImagesDir, "*", SearchOption.AllDirectories);
				foreach (var src in imageFiles)
				{
					var relativePath = Path.GetRelativePath(extractedImagesDir, src);
					var dest = Path.Combine(imageDir, relativePath);
					var destDir = Path.GetDirectoryName(dest);
					if (!string.IsNullOrEmpty(destDir))
					{
						Directory.CreateDirectory(destDir);
					}
					File.Copy(src, dest, overwrite: true);
					imagesRestored++;
				}
			}

			return new TmdbRestoreResultDto(inserted, updated, skipped, imagesRestored, reasons);
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir))
				{
					Directory.Delete(tempDir, recursive: true);
				}
			}
			catch
			{
				// best-effort
			}
		}
	}
}
