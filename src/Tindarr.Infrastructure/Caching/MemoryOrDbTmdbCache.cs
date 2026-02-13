using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Tindarr.Application.Abstractions.Caching;

namespace Tindarr.Infrastructure.Caching;

/// <summary>
/// Two-tier TMDB cache:
/// - in-memory for fast reads within a process
/// - persistent sqlite cache (tmdbmetadata.db) to avoid repeated upstream calls across restarts
/// </summary>
public sealed class MemoryOrDbTmdbCache(IMemoryCache memoryCache, string sqliteDbPath) : ITmdbCache, ITmdbCacheAdmin
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private const int BusyTimeoutMs = 5_000;
	private const int WriteRetryCount = 6;
	private static readonly TimeSpan WriteRetryBaseDelay = TimeSpan.FromMilliseconds(50);
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private bool _initialized;

	private readonly SemaphoreSlim _maintenanceLock = new(1, 1);
	private DateTimeOffset _lastMaintenanceUtc = DateTimeOffset.MinValue;
	private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(10);
	private const int DefaultMaxCacheRowCount = 5000;
	private const int MinMaxCacheRowCount = 200;
	private const int MaxMaxCacheRowCount = 200_000;
	private const string SettingsKeyMaxRows = "max_rows";

	public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
	{
		if (memoryCache.TryGetValue(key, out T? value))
		{
			return value;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT payload_json, expires_at_utc
			FROM tmdb_cache
			WHERE cache_key = $key AND payload_type = $type
			LIMIT 1;
			""";
		cmd.Parameters.AddWithValue("$key", key);
		cmd.Parameters.AddWithValue("$type", typeof(T).FullName ?? typeof(T).Name);

		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			return default;
		}

		var payloadJson = reader.IsDBNull(0) ? null : reader.GetString(0);
		var expiresAtStr = reader.IsDBNull(1) ? null : reader.GetString(1);

		if (string.IsNullOrWhiteSpace(payloadJson) || string.IsNullOrWhiteSpace(expiresAtStr)
			|| !DateTimeOffset.TryParse(expiresAtStr, out var expiresAtUtc))
		{
			return default;
		}

		var now = DateTimeOffset.UtcNow;
		if (expiresAtUtc <= now)
		{
			// Best-effort cleanup of expired entry.
			var del = conn.CreateCommand();
			del.CommandText = "DELETE FROM tmdb_cache WHERE cache_key = $key AND payload_type = $type;";
			del.Parameters.AddWithValue("$key", key);
			del.Parameters.AddWithValue("$type", typeof(T).FullName ?? typeof(T).Name);
			await ExecuteNonQueryWithRetryAsync(del, cancellationToken).ConfigureAwait(false);
			return default;
		}

		T? parsed;
		try
		{
			parsed = JsonSerializer.Deserialize<T>(payloadJson, Json);
		}
		catch (JsonException)
		{
			return default;
		}

		if (parsed is null)
		{
			return default;
		}

		// Warm the in-memory cache for the remaining TTL.
		var remaining = expiresAtUtc - now;
		if (remaining > TimeSpan.Zero)
		{
			memoryCache.Set(key, parsed, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = remaining });
		}

		return parsed;
	}

	public async ValueTask SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
	{
		if (ttl <= TimeSpan.Zero)
		{
			return;
		}

		// Always write-through to memory.
		memoryCache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		var payloadJson = JsonSerializer.Serialize(value, Json);
		var now = DateTimeOffset.UtcNow;
		var expiresAt = now + ttl;

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			INSERT INTO tmdb_cache (cache_key, payload_type, payload_json, created_at_utc, expires_at_utc)
			VALUES ($key, $type, $json, $created, $expires)
			ON CONFLICT(cache_key, payload_type) DO UPDATE SET
				payload_json = excluded.payload_json,
				created_at_utc = excluded.created_at_utc,
				expires_at_utc = excluded.expires_at_utc;
			""";
		cmd.Parameters.AddWithValue("$key", key);
		cmd.Parameters.AddWithValue("$type", typeof(T).FullName ?? typeof(T).Name);
		cmd.Parameters.AddWithValue("$json", payloadJson);
		cmd.Parameters.AddWithValue("$created", now.ToString("O"));
		cmd.Parameters.AddWithValue("$expires", expiresAt.ToString("O"));

		await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);
	}

	private SqliteConnection CreateConnection()
	{
		// DataSource is a file path.
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
		// These pragmas improve multi-process concurrency (API + workers) and reduce transient "database is locked" errors.
		// - WAL allows concurrent readers while a writer is active.
		// - busy_timeout causes SQLite to wait before failing with SQLITE_BUSY/SQLITE_LOCKED.
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
			// Best-effort only; cache should keep working even if pragmas fail.
		}
	}

	private static bool IsTransientLock(SqliteException ex)
	{
		// SQLITE_BUSY (5), SQLITE_LOCKED (6)
		return ex.SqliteErrorCode is 5 or 6;
	}

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

			var dir = Path.GetDirectoryName(sqliteDbPath);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				Directory.CreateDirectory(dir);
			}

			await using var conn = CreateConnection();
			await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
			await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

			var cmd = conn.CreateCommand();
			cmd.CommandText = """
				CREATE TABLE IF NOT EXISTS tmdb_cache (
					cache_key TEXT NOT NULL,
					payload_type TEXT NOT NULL,
					payload_json TEXT NOT NULL,
					created_at_utc TEXT NOT NULL,
					expires_at_utc TEXT NOT NULL,
					PRIMARY KEY (cache_key, payload_type)
				);

				CREATE INDEX IF NOT EXISTS ix_tmdb_cache_expires_at_utc
				ON tmdb_cache (expires_at_utc);

				CREATE TABLE IF NOT EXISTS tmdb_cache_settings (
					settings_key TEXT NOT NULL PRIMARY KEY,
					settings_value TEXT NOT NULL,
					updated_at_utc TEXT NOT NULL
				);
				""";

			await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);

			// Seed defaults (best-effort).
			try
			{
				var seed = conn.CreateCommand();
				seed.CommandText = """
					INSERT OR IGNORE INTO tmdb_cache_settings (settings_key, settings_value, updated_at_utc)
					VALUES ($k, $v, $now);
					""";
				seed.Parameters.AddWithValue("$k", SettingsKeyMaxRows);
				seed.Parameters.AddWithValue("$v", DefaultMaxCacheRowCount.ToString());
				seed.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
				await ExecuteNonQueryWithRetryAsync(seed, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				// Best-effort only.
			}
			_initialized = true;
		}
		finally
		{
			_initLock.Release();
		}
	}

	private static int ClampMaxRows(int value)
	{
		if (value <= 0)
		{
			return DefaultMaxCacheRowCount;
		}

		return Math.Clamp(value, MinMaxCacheRowCount, MaxMaxCacheRowCount);
	}

	private static async ValueTask<int> GetMaxRowsAsync(SqliteConnection conn, CancellationToken cancellationToken)
	{
		try
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = """
				SELECT settings_value
				FROM tmdb_cache_settings
				WHERE settings_key = $k
				LIMIT 1;
				""";
			cmd.Parameters.AddWithValue("$k", SettingsKeyMaxRows);

			var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
			if (raw is null)
			{
				return DefaultMaxCacheRowCount;
			}

			if (raw is string s && int.TryParse(s, out var parsed))
			{
				return ClampMaxRows(parsed);
			}

			if (raw is long l)
			{
				return ClampMaxRows((int)Math.Clamp(l, int.MinValue, int.MaxValue));
			}
		}
		catch
		{
			// ignore
		}

		return DefaultMaxCacheRowCount;
	}

	private static async ValueTask SetMaxRowsAsync(SqliteConnection conn, int maxRows, CancellationToken cancellationToken)
	{
		maxRows = ClampMaxRows(maxRows);
		var now = DateTimeOffset.UtcNow.ToString("O");
		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			INSERT INTO tmdb_cache_settings (settings_key, settings_value, updated_at_utc)
			VALUES ($k, $v, $now)
			ON CONFLICT(settings_key) DO UPDATE SET
				settings_value = excluded.settings_value,
				updated_at_utc = excluded.updated_at_utc;
			""";
		cmd.Parameters.AddWithValue("$k", SettingsKeyMaxRows);
		cmd.Parameters.AddWithValue("$v", maxRows.ToString());
		cmd.Parameters.AddWithValue("$now", now);
		await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);
	}

	private async Task MaybeRunMaintenanceAsync(SqliteConnection conn, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		if (now - _lastMaintenanceUtc < MaintenanceInterval)
		{
			return;
		}

		await _maintenanceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			// Re-check under lock in case another request already cleaned up.
			now = DateTimeOffset.UtcNow;
			if (now - _lastMaintenanceUtc < MaintenanceInterval)
			{
				return;
			}

			// Keep the DB bounded over time (prune expired entries + cap total rows).
			try
			{
				var maxRows = await GetMaxRowsAsync(conn, cancellationToken).ConfigureAwait(false);
				{
					var deleteExpiredCmd = conn.CreateCommand();
					deleteExpiredCmd.CommandText = """
						DELETE FROM tmdb_cache
						WHERE expires_at_utc < $now;
						""";
					deleteExpiredCmd.Parameters.AddWithValue("$now", now.ToString("O"));
					await ExecuteNonQueryWithRetryAsync(deleteExpiredCmd, cancellationToken).ConfigureAwait(false);
				}

				{
					var trimCmd = conn.CreateCommand();
					trimCmd.CommandText = """
						DELETE FROM tmdb_cache
						WHERE rowid IN (
							SELECT rowid
							FROM tmdb_cache
							ORDER BY expires_at_utc DESC
							LIMIT -1 OFFSET $maxRows
						);
						""";
					trimCmd.Parameters.AddWithValue("$maxRows", maxRows);
					await ExecuteNonQueryWithRetryAsync(trimCmd, cancellationToken).ConfigureAwait(false);
				}
			}
			catch
			{
				// Best-effort maintenance only; never fail caller operations due to cleanup.
			}

			_lastMaintenanceUtc = now;
		}
		finally
		{
			_maintenanceLock.Release();
		}
	}

	public async ValueTask<int> GetMaxRowsAsync(CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		return await GetMaxRowsAsync(conn, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask SetMaxRowsAsync(int maxRows, CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await SetMaxRowsAsync(conn, maxRows, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<int> GetRowCountAsync(CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		try
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT COUNT(*) FROM tmdb_cache;";
			var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
			return raw switch
			{
				long l => (int)Math.Clamp(l, 0, int.MaxValue),
				int i => i,
				_ => 0
			};
		}
		catch
		{
			return 0;
		}
	}
}

