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
public sealed class MemoryOrDbTmdbCache(IMemoryCache memoryCache, string sqliteDbPath) : ITmdbCache
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private bool _initialized;

	public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
	{
		if (memoryCache.TryGetValue(key, out T? value))
		{
			return value;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

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
			await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

		await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	private SqliteConnection CreateConnection()
	{
		// DataSource is a file path.
		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = sqliteDbPath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Shared
		};
		return new SqliteConnection(builder.ToString());
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
				""";

			await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			_initialized = true;
		}
		finally
		{
			_initLock.Release();
		}
	}
}

