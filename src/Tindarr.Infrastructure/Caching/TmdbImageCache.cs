using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Caching;

public sealed class TmdbImageCache(HttpClient httpClient, IOptions<TmdbOptions> options, string sqliteDbPath) : ITmdbImageCache
{
	private const int BusyTimeoutMs = 5_000;
	private const int WriteRetryCount = 6;
	private static readonly TimeSpan WriteRetryBaseDelay = TimeSpan.FromMilliseconds(50);
	private readonly TmdbOptions _tmdb = options.Value;

	private readonly SemaphoreSlim _initLock = new(1, 1);
	private bool _initialized;

	private string ImageDir
	{
		get
		{
			var dbDir = Path.GetDirectoryName(sqliteDbPath);
			return Path.Combine(string.IsNullOrWhiteSpace(dbDir) ? AppContext.BaseDirectory : dbDir, "tmdb-images");
		}
	}

	private SqliteConnection CreateConnection()
	{
		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = sqliteDbPath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Shared,
			DefaultTimeout = (int)Math.Ceiling(BusyTimeoutMs / 1000.0)
		};
		return new SqliteConnection(builder.ToString());
	}

	private static async Task ApplyConnectionPragmasAsync(SqliteConnection conn, CancellationToken cancellationToken)
	{
		try
		{
			var pragma = conn.CreateCommand();
			pragma.CommandText = $"""
				PRAGMA journal_mode=WAL;
				PRAGMA synchronous=NORMAL;
				PRAGMA busy_timeout={BusyTimeoutMs};
			""";
			await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			// best-effort
		}
	}

	private static bool IsTransientLock(SqliteException ex) => ex.SqliteErrorCode is 5 or 6;

	private static async Task ExecuteNonQueryWithRetryAsync(SqliteCommand command, CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt <= WriteRetryCount; attempt++)
		{
			try
			{
				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				return;
			}
			catch (SqliteException ex) when (IsTransientLock(ex) && attempt < WriteRetryCount)
			{
				var delay = TimeSpan.FromMilliseconds(WriteRetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
	{
		if (_initialized)
		{
			return;
		}

		await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_initialized)
			{
				return;
			}

			Directory.CreateDirectory(ImageDir);

			await using var conn = CreateConnection();
			await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
			await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

			var cmd = conn.CreateCommand();
			cmd.CommandText = """
				CREATE TABLE IF NOT EXISTS tmdb_image_cache (
					cache_key TEXT NOT NULL PRIMARY KEY,
					tmdb_path TEXT NOT NULL,
					size TEXT NOT NULL,
					file_name TEXT NOT NULL,
					bytes INTEGER NOT NULL,
					created_at_utc TEXT NOT NULL,
					last_access_utc TEXT NOT NULL
				);

				CREATE INDEX IF NOT EXISTS ix_tmdb_image_cache_last_access
				ON tmdb_image_cache (last_access_utc);
			""";
			await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);

			_initialized = true;
		}
		finally
		{
			_initLock.Release();
		}
	}

	private static string NormalizeSize(string size)
	{
		var s = (size ?? string.Empty).Trim().Trim('/');
		return string.IsNullOrWhiteSpace(s) ? "original" : s;
	}

	private static string NormalizePath(string path)
	{
		var p = (path ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(p))
		{
			return string.Empty;
		}
		return p.StartsWith('/') ? p : "/" + p;
	}

	private static string GetCacheKey(string size, string path) => $"{size}:{path}";

	private static string ComputeSha256Hex(string input)
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes(input);
		var hash = SHA256.HashData(bytes);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static string GuessContentType(string path)
	{
		var ext = Path.GetExtension(path);
		return ext.ToLowerInvariant() switch
		{
			".jpg" or ".jpeg" => "image/jpeg",
			".png" => "image/png",
			".webp" => "image/webp",
			_ => "application/octet-stream"
		};
	}

	public async Task<TmdbImageCacheResult?> GetOrFetchAsync(string size, string path, CancellationToken cancellationToken)
	{
		if (!_tmdb.HasCredentials)
		{
			return null;
		}

		size = NormalizeSize(size);
		path = NormalizePath(path);
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		var cacheKey = GetCacheKey(size, path);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

		// Try DB hit.
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = """
				SELECT file_name
				FROM tmdb_image_cache
				WHERE cache_key = $k
				LIMIT 1;
			""";
			cmd.Parameters.AddWithValue("$k", cacheKey);

			var existing = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
			if (!string.IsNullOrWhiteSpace(existing))
			{
				var filePath = Path.Combine(ImageDir, existing);
				if (File.Exists(filePath))
				{
					var touch = conn.CreateCommand();
					touch.CommandText = "UPDATE tmdb_image_cache SET last_access_utc = $now WHERE cache_key = $k;";
					touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
					touch.Parameters.AddWithValue("$k", cacheKey);
					await ExecuteNonQueryWithRetryAsync(touch, cancellationToken).ConfigureAwait(false);
					return new TmdbImageCacheResult(filePath, GuessContentType(path));
				}
			}
		}

		// Download.
		var normalizedBase = _tmdb.ImageBaseUrl.TrimEnd('/');
		var requestUri = $"{normalizedBase}/{size}{path}";
		using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
		if (bytes.Length == 0)
		{
			return null;
		}

		var ext = Path.GetExtension(path);
		var fileName = ComputeSha256Hex(cacheKey) + (string.IsNullOrWhiteSpace(ext) ? ".img" : ext);
		var filePathFinal = Path.Combine(ImageDir, fileName);
		var tempPath = filePathFinal + ".tmp";
		await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
		File.Move(tempPath, filePathFinal, overwrite: true);

		var now = DateTimeOffset.UtcNow.ToString("O");
		var upsert = conn.CreateCommand();
		upsert.CommandText = """
			INSERT INTO tmdb_image_cache (cache_key, tmdb_path, size, file_name, bytes, created_at_utc, last_access_utc)
			VALUES ($k, $path, $size, $file, $bytes, $now, $now)
			ON CONFLICT(cache_key) DO UPDATE SET
				file_name = excluded.file_name,
				bytes = excluded.bytes,
				last_access_utc = excluded.last_access_utc;
		""";
		upsert.Parameters.AddWithValue("$k", cacheKey);
		upsert.Parameters.AddWithValue("$path", path);
		upsert.Parameters.AddWithValue("$size", size);
		upsert.Parameters.AddWithValue("$file", fileName);
		upsert.Parameters.AddWithValue("$bytes", bytes.Length);
		upsert.Parameters.AddWithValue("$now", now);
		await ExecuteNonQueryWithRetryAsync(upsert, cancellationToken).ConfigureAwait(false);

		return new TmdbImageCacheResult(filePathFinal, GuessContentType(path));
	}

	public async Task<bool> HasAsync(string size, string path, CancellationToken cancellationToken)
	{
		size = NormalizeSize(size);
		path = NormalizePath(path);
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		var cacheKey = GetCacheKey(size, path);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

		try
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = """
				SELECT file_name
				FROM tmdb_image_cache
				WHERE cache_key = $k
				LIMIT 1;
			""";
			cmd.Parameters.AddWithValue("$k", cacheKey);
			var existing = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
			if (string.IsNullOrWhiteSpace(existing))
			{
				return false;
			}

			var filePath = Path.Combine(ImageDir, existing);
			return File.Exists(filePath);
		}
		catch
		{
			return false;
		}
	}

	public async ValueTask<long> GetTotalBytesAsync(CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

		try
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT COALESCE(SUM(bytes), 0) FROM tmdb_image_cache;";
			var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
			return raw switch
			{
				long l => Math.Max(0, l),
				int i => Math.Max(0, i),
				_ => 0
			};
		}
		catch
		{
			return 0;
		}
	}

	public async Task PruneAsync(long maxBytes, CancellationToken cancellationToken)
	{
		maxBytes = Math.Max(0, maxBytes);
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

		long total;
		try
		{
			var sumCmd = conn.CreateCommand();
			sumCmd.CommandText = "SELECT COALESCE(SUM(bytes), 0) FROM tmdb_image_cache;";
			var raw = await sumCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
			total = raw switch
			{
				long l => Math.Max(0, l),
				int i => Math.Max(0, i),
				_ => 0
			};
		}
		catch
		{
			return;
		}

		if (total <= maxBytes)
		{
			return;
		}

		// Delete oldest until we're under budget.
		while (total > maxBytes)
		{
			string? fileName;
			long bytes;
			{
				var next = conn.CreateCommand();
				next.CommandText = """
					SELECT file_name, bytes
					FROM tmdb_image_cache
					ORDER BY last_access_utc ASC
					LIMIT 1;
				""";
				await using var reader = await next.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
				if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
				{
					break;
				}
				fileName = reader.IsDBNull(0) ? null : reader.GetString(0);
				bytes = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
			}

			if (string.IsNullOrWhiteSpace(fileName))
			{
				break;
			}

			var del = conn.CreateCommand();
			del.CommandText = "DELETE FROM tmdb_image_cache WHERE file_name = $f;";
			del.Parameters.AddWithValue("$f", fileName);
			await ExecuteNonQueryWithRetryAsync(del, cancellationToken).ConfigureAwait(false);

			try
			{
				var fp = Path.Combine(ImageDir, fileName);
				if (File.Exists(fp))
				{
					File.Delete(fp);
				}
			}
			catch
			{
				// ignore file deletion errors
			}

			total -= Math.Max(0, bytes);
		}
	}
}
