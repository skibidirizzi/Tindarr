using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Contracts.Movies;

namespace Tindarr.Infrastructure.Integrations.Tmdb;

public sealed class TmdbMetadataStore(string sqliteDbPath) : ITmdbMetadataStore
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

	private const string SettingsKeyMaxMovies = "movie_max_count";
	private const string SettingsKeyMaxPoolPerUser = "pool_max_per_user";
	private const string SettingsKeyImageCacheMaxMb = "image_cache_max_mb";
	private const string SettingsKeyPosterMode = "poster_mode";

	private const int DefaultMaxMovies = 20_000;
	private const int MinMaxMovies = 500;
	private const int MaxMaxMovies = 500_000;

	private const int DefaultMaxPoolPerUser = 2_000;
	private const int MinMaxPoolPerUser = 50;
	private const int MaxMaxPoolPerUser = 50_000;

	private const int DefaultImageCacheMaxMb = 512;
	private const int MinImageCacheMaxMb = 0;
	private const int MaxImageCacheMaxMb = 100_000;

	private const TmdbPosterMode DefaultPosterMode = TmdbPosterMode.Tmdb;

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
				PRAGMA foreign_keys=ON;
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
				CREATE TABLE IF NOT EXISTS tmdb_movies (
					tmdb_id INTEGER NOT NULL PRIMARY KEY,
					title TEXT NOT NULL,
					original_title TEXT NULL,
					overview TEXT NULL,
					poster_path TEXT NULL,
					backdrop_path TEXT NULL,
					release_date TEXT NULL,
					release_year INTEGER NULL,
					original_language TEXT NULL,
					rating REAL NULL,
					genre_ids_json TEXT NULL,
					genres_json TEXT NULL,
					details_fetched_at_utc TEXT NULL,
					updated_at_utc TEXT NOT NULL
				);

				CREATE INDEX IF NOT EXISTS ix_tmdb_movies_updated_at
				ON tmdb_movies (updated_at_utc);

				CREATE TABLE IF NOT EXISTS tmdb_user_pool (
					user_id TEXT NOT NULL,
					tmdb_id INTEGER NOT NULL,
					rank INTEGER NOT NULL,
					added_at_utc TEXT NOT NULL,
					PRIMARY KEY (user_id, tmdb_id),
					FOREIGN KEY (tmdb_id) REFERENCES tmdb_movies (tmdb_id) ON DELETE CASCADE
				);

				CREATE INDEX IF NOT EXISTS ix_tmdb_user_pool_user
				ON tmdb_user_pool (user_id, rank);

				-- Reuse tmdb_cache_settings table created by MemoryOrDbTmdbCache.
				CREATE TABLE IF NOT EXISTS tmdb_cache_settings (
					settings_key TEXT NOT NULL PRIMARY KEY,
					settings_value TEXT NOT NULL,
					updated_at_utc TEXT NOT NULL
				);
			""";

			await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);

			await SeedDefaultSettingAsync(conn, SettingsKeyMaxMovies, DefaultMaxMovies.ToString(), cancellationToken).ConfigureAwait(false);
			await SeedDefaultSettingAsync(conn, SettingsKeyMaxPoolPerUser, DefaultMaxPoolPerUser.ToString(), cancellationToken).ConfigureAwait(false);
			await SeedDefaultSettingAsync(conn, SettingsKeyImageCacheMaxMb, DefaultImageCacheMaxMb.ToString(), cancellationToken).ConfigureAwait(false);
			await SeedDefaultSettingAsync(conn, SettingsKeyPosterMode, DefaultPosterMode.ToString(), cancellationToken).ConfigureAwait(false);

			_initialized = true;
		}
		finally
		{
			_initLock.Release();
		}
	}

	private static async Task SeedDefaultSettingAsync(SqliteConnection conn, string key, string value, CancellationToken cancellationToken)
	{
		try
		{
			var seed = conn.CreateCommand();
			seed.CommandText = """
				INSERT OR IGNORE INTO tmdb_cache_settings (settings_key, settings_value, updated_at_utc)
				VALUES ($k, $v, $now);
			""";
			seed.Parameters.AddWithValue("$k", key);
			seed.Parameters.AddWithValue("$v", value);
			seed.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
			await ExecuteNonQueryWithRetryAsync(seed, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			// best-effort
		}
	}

	private static int Clamp(int value, int min, int max, int fallback)
	{
		if (value <= 0)
		{
			return fallback;
		}
		return Math.Clamp(value, min, max);
	}

	private static TmdbPosterMode ParsePosterMode(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return DefaultPosterMode;
		}

		return Enum.TryParse<TmdbPosterMode>(raw, ignoreCase: true, out var parsed) ? parsed : DefaultPosterMode;
	}

	private static async ValueTask<string?> GetSettingRawAsync(SqliteConnection conn, string key, CancellationToken cancellationToken)
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
			cmd.Parameters.AddWithValue("$k", key);
			var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
			return raw switch
			{
				string s => s,
				long l => l.ToString(),
				int i => i.ToString(),
				_ => null
			};
		}
		catch
		{
			return null;
		}
	}

	private static async ValueTask SetSettingRawAsync(SqliteConnection conn, string key, string value, CancellationToken cancellationToken)
	{
		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			INSERT INTO tmdb_cache_settings (settings_key, settings_value, updated_at_utc)
			VALUES ($k, $v, $now)
			ON CONFLICT(settings_key) DO UPDATE SET
				settings_value = excluded.settings_value,
				updated_at_utc = excluded.updated_at_utc;
		""";
		cmd.Parameters.AddWithValue("$k", key);
		cmd.Parameters.AddWithValue("$v", value);
		cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
		await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<TmdbMetadataSettings> GetSettingsAsync(CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

		var maxMovies = Clamp(int.TryParse(await GetSettingRawAsync(conn, SettingsKeyMaxMovies, cancellationToken), out var mm) ? mm : DefaultMaxMovies,
			MinMaxMovies,
			MaxMaxMovies,
			DefaultMaxMovies);

		var maxPool = Clamp(int.TryParse(await GetSettingRawAsync(conn, SettingsKeyMaxPoolPerUser, cancellationToken), out var mp) ? mp : DefaultMaxPoolPerUser,
			MinMaxPoolPerUser,
			MaxMaxPoolPerUser,
			DefaultMaxPoolPerUser);

		var maxMb = Clamp(int.TryParse(await GetSettingRawAsync(conn, SettingsKeyImageCacheMaxMb, cancellationToken), out var im) ? im : DefaultImageCacheMaxMb,
			MinImageCacheMaxMb,
			MaxImageCacheMaxMb,
			DefaultImageCacheMaxMb);

		var posterMode = ParsePosterMode(await GetSettingRawAsync(conn, SettingsKeyPosterMode, cancellationToken));

		return new TmdbMetadataSettings(maxMovies, maxPool, maxMb, posterMode);
	}

	public async ValueTask<TmdbMetadataSettings> SetSettingsAsync(TmdbMetadataSettings settings, CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		var normalized = new TmdbMetadataSettings(
			MaxMovies: Clamp(settings.MaxMovies, MinMaxMovies, MaxMaxMovies, DefaultMaxMovies),
			MaxPoolPerUser: Clamp(settings.MaxPoolPerUser, MinMaxPoolPerUser, MaxMaxPoolPerUser, DefaultMaxPoolPerUser),
			ImageCacheMaxMb: Clamp(settings.ImageCacheMaxMb, MinImageCacheMaxMb, MaxImageCacheMaxMb, DefaultImageCacheMaxMb),
			PosterMode: settings.PosterMode);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);

		await SetSettingRawAsync(conn, SettingsKeyMaxMovies, normalized.MaxMovies.ToString(), cancellationToken).ConfigureAwait(false);
		await SetSettingRawAsync(conn, SettingsKeyMaxPoolPerUser, normalized.MaxPoolPerUser.ToString(), cancellationToken).ConfigureAwait(false);
		await SetSettingRawAsync(conn, SettingsKeyImageCacheMaxMb, normalized.ImageCacheMaxMb.ToString(), cancellationToken).ConfigureAwait(false);
		await SetSettingRawAsync(conn, SettingsKeyPosterMode, normalized.PosterMode.ToString(), cancellationToken).ConfigureAwait(false);

		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);
		return normalized;
	}

	public async ValueTask<TmdbMetadataStats> GetStatsAsync(CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT COUNT(*) FROM tmdb_movies;";
		var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
		var count = raw switch
		{
			long l => (int)Math.Clamp(l, 0, int.MaxValue),
			int i => i,
			_ => 0
		};

		return new TmdbMetadataStats(MovieCount: count, ImageCacheBytes: 0);
	}

	public async Task AddToUserPoolAsync(string userId, IReadOnlyList<TmdbDiscoverMovieRecord> discovered, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(userId) || discovered.Count == 0)
		{
			return;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);

		using var tx = conn.BeginTransaction();
		try
		{
			var now = DateTimeOffset.UtcNow.ToString("O");
			for (var i = 0; i < discovered.Count; i++)
			{
				var m = discovered[i];
				if (m.Id <= 0)
				{
					continue;
				}

				var title = (m.Title ?? m.OriginalTitle ?? $"TMDB:{m.Id}").Trim();
				if (string.IsNullOrWhiteSpace(title))
				{
					title = $"TMDB:{m.Id}";
				}

				var year = TryParseYear(m.ReleaseDate);
				var genreIdsJson = m.GenreIds is { Count: > 0 } ? JsonSerializer.Serialize(m.GenreIds, Json) : null;

				var upsertMovie = conn.CreateCommand();
				upsertMovie.Transaction = tx;
				upsertMovie.CommandText = """
					INSERT INTO tmdb_movies (
						tmdb_id, title, original_title, overview, poster_path, backdrop_path,
						release_date, release_year, original_language, rating, genre_ids_json, updated_at_utc
					)
					VALUES (
						$tmdbId, $title, $originalTitle, $overview, $posterPath, $backdropPath,
						$releaseDate, $releaseYear, $originalLanguage, $rating, $genreIdsJson, $updatedAt
					)
					ON CONFLICT(tmdb_id) DO UPDATE SET
						title = excluded.title,
						original_title = excluded.original_title,
						overview = excluded.overview,
						poster_path = excluded.poster_path,
						backdrop_path = excluded.backdrop_path,
						release_date = excluded.release_date,
						release_year = excluded.release_year,
						original_language = excluded.original_language,
						rating = excluded.rating,
						genre_ids_json = COALESCE(excluded.genre_ids_json, tmdb_movies.genre_ids_json),
						updated_at_utc = excluded.updated_at_utc;
				""";
				upsertMovie.Parameters.AddWithValue("$tmdbId", m.Id);
				upsertMovie.Parameters.AddWithValue("$title", title);
				upsertMovie.Parameters.AddWithValue("$originalTitle", (object?)m.OriginalTitle ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$overview", (object?)m.Overview ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$posterPath", (object?)m.PosterPath ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$backdropPath", (object?)m.BackdropPath ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$releaseDate", (object?)m.ReleaseDate ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$releaseYear", (object?)year ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$originalLanguage", (object?)m.OriginalLanguage ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$rating", (object?)m.VoteAverage ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$genreIdsJson", (object?)genreIdsJson ?? DBNull.Value);
				upsertMovie.Parameters.AddWithValue("$updatedAt", now);
				await ExecuteNonQueryWithRetryAsync(upsertMovie, cancellationToken).ConfigureAwait(false);

				var upsertPool = conn.CreateCommand();
				upsertPool.Transaction = tx;
				upsertPool.CommandText = """
					INSERT INTO tmdb_user_pool (user_id, tmdb_id, rank, added_at_utc)
					VALUES ($userId, $tmdbId, $rank, $now)
					ON CONFLICT(user_id, tmdb_id) DO UPDATE SET
						rank = excluded.rank,
						added_at_utc = excluded.added_at_utc;
				""";
				upsertPool.Parameters.AddWithValue("$userId", userId);
				upsertPool.Parameters.AddWithValue("$tmdbId", m.Id);
				upsertPool.Parameters.AddWithValue("$rank", i);
				upsertPool.Parameters.AddWithValue("$now", now);
				await ExecuteNonQueryWithRetryAsync(upsertPool, cancellationToken).ConfigureAwait(false);
			}

			// Trim pool for the user.
			var trimPool = conn.CreateCommand();
			trimPool.Transaction = tx;
			trimPool.CommandText = """
				DELETE FROM tmdb_user_pool
				WHERE rowid IN (
					SELECT rowid
					FROM tmdb_user_pool
					WHERE user_id = $userId
					ORDER BY rank ASC, added_at_utc DESC
					LIMIT -1 OFFSET $max
				);
			""";
			trimPool.Parameters.AddWithValue("$userId", userId);
			trimPool.Parameters.AddWithValue("$max", settings.MaxPoolPerUser);
			await ExecuteNonQueryWithRetryAsync(trimPool, cancellationToken).ConfigureAwait(false);

			tx.Commit();
		}
		catch
		{
			try
			{
				tx.Rollback();
			}
			catch
			{
				// ignore
			}
			throw;
		}
	}

	public async Task ClearUserPoolAsync(string userId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(userId))
		{
			return;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM tmdb_user_pool WHERE user_id = $userId;";
		cmd.Parameters.AddWithValue("$userId", userId);
		await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<TmdbDiscoverMovieRecord>> GetUserPoolAsync(string userId, int limit, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(userId) || limit <= 0)
		{
			return [];
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT m.tmdb_id, m.title, m.original_title, m.overview, m.poster_path, m.backdrop_path,
				m.release_date, m.original_language, m.rating, m.genre_ids_json
			FROM tmdb_user_pool p
			JOIN tmdb_movies m ON m.tmdb_id = p.tmdb_id
			WHERE p.user_id = $userId
			ORDER BY p.rank ASC, p.added_at_utc DESC
			LIMIT $limit;
		""";
		cmd.Parameters.AddWithValue("$userId", userId);
		cmd.Parameters.AddWithValue("$limit", limit);

		var results = new List<TmdbDiscoverMovieRecord>(capacity: limit);
		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var id = reader.GetInt32(0);
			var title = reader.IsDBNull(1) ? null : reader.GetString(1);
			var originalTitle = reader.IsDBNull(2) ? null : reader.GetString(2);
			var overview = reader.IsDBNull(3) ? null : reader.GetString(3);
			var posterPath = reader.IsDBNull(4) ? null : reader.GetString(4);
			var backdropPath = reader.IsDBNull(5) ? null : reader.GetString(5);
			var releaseDate = reader.IsDBNull(6) ? null : reader.GetString(6);
			var originalLanguage = reader.IsDBNull(7) ? null : reader.GetString(7);
			double? rating = reader.IsDBNull(8) ? null : reader.GetDouble(8);
			var genreIdsJson = reader.IsDBNull(9) ? null : reader.GetString(9);

			IReadOnlyList<int>? genreIds = null;
			if (!string.IsNullOrWhiteSpace(genreIdsJson))
			{
				try
				{
					genreIds = JsonSerializer.Deserialize<List<int>>(genreIdsJson, Json);
				}
				catch
				{
					genreIds = null;
				}
			}

			results.Add(new TmdbDiscoverMovieRecord(
				Id: id,
				Title: title,
				OriginalTitle: originalTitle,
				Overview: overview,
				PosterPath: posterPath,
				BackdropPath: backdropPath,
				ReleaseDate: releaseDate,
				OriginalLanguage: originalLanguage,
				VoteAverage: rating,
				GenreIds: genreIds));
		}

		return results;
	}

	public async Task<IReadOnlyList<int>> ListMoviesNeedingDetailsAsync(int limit, CancellationToken cancellationToken)
	{
		limit = Math.Clamp(limit, 1, 1000);
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT tmdb_id
			FROM tmdb_movies
			WHERE details_fetched_at_utc IS NULL
			ORDER BY updated_at_utc DESC
			LIMIT $limit;
		""";
		cmd.Parameters.AddWithValue("$limit", limit);

		var ids = new List<int>(capacity: limit);
		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			ids.Add(reader.GetInt32(0));
		}

		return ids;
	}

	public async Task UpdateMovieDetailsAsync(MovieDetailsDto details, CancellationToken cancellationToken)
	{
		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var genresJson = details.Genres is { Count: > 0 } ? JsonSerializer.Serialize(details.Genres, Json) : null;

		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			UPDATE tmdb_movies
			SET genres_json = $genresJson,
				details_fetched_at_utc = $now,
				updated_at_utc = $now
			WHERE tmdb_id = $tmdbId;
		""";
		cmd.Parameters.AddWithValue("$genresJson", (object?)genresJson ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
		cmd.Parameters.AddWithValue("$tmdbId", details.TmdbId);
		await ExecuteNonQueryWithRetryAsync(cmd, cancellationToken).ConfigureAwait(false);
	}

	public async Task<TmdbStoredMovie?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
	{
		if (tmdbId <= 0)
		{
			return null;
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT tmdb_id, title, release_year, poster_path, backdrop_path,
				details_fetched_at_utc, updated_at_utc
			FROM tmdb_movies
			WHERE tmdb_id = $id
			LIMIT 1;
		""";
		cmd.Parameters.AddWithValue("$id", tmdbId);

		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			return null;
		}

		var id = reader.GetInt32(0);
		var title = reader.IsDBNull(1) ? $"TMDB:{id}" : reader.GetString(1);
		int? year = reader.IsDBNull(2) ? null : reader.GetInt32(2);
		var posterPath = reader.IsDBNull(3) ? null : reader.GetString(3);
		var backdropPath = reader.IsDBNull(4) ? null : reader.GetString(4);
		var detailsFetchedRaw = reader.IsDBNull(5) ? null : reader.GetString(5);
		var updatedRaw = reader.IsDBNull(6) ? null : reader.GetString(6);

		DateTimeOffset? detailsFetchedAt = null;
		if (!string.IsNullOrWhiteSpace(detailsFetchedRaw) && DateTimeOffset.TryParse(detailsFetchedRaw, out var parsedDetails))
		{
			detailsFetchedAt = parsedDetails;
		}

		DateTimeOffset? updatedAt = null;
		if (!string.IsNullOrWhiteSpace(updatedRaw) && DateTimeOffset.TryParse(updatedRaw, out var parsedUpdated))
		{
			updatedAt = parsedUpdated;
		}

		return new TmdbStoredMovie(
			TmdbId: id,
			Title: title,
			ReleaseYear: year,
			PosterPath: posterPath,
			BackdropPath: backdropPath,
			DetailsFetchedAtUtc: detailsFetchedAt,
			UpdatedAtUtc: updatedAt);
	}

	public async Task<IReadOnlyList<TmdbStoredMovie>> ListMoviesAsync(
		int skip,
		int take,
		bool missingDetailsOnly,
		string? titleQuery,
		CancellationToken cancellationToken)
	{
		skip = Math.Clamp(skip, 0, 2_000_000_000);
		take = Math.Clamp(take, 1, 500);
		var q = (titleQuery ?? string.Empty).Trim();
		if (q.Length > 200)
		{
			q = q[..200];
		}

		await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
		await using var conn = CreateConnection();
		await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
		await ApplyConnectionPragmasAsync(conn, cancellationToken).ConfigureAwait(false);
		await MaybeRunMaintenanceAsync(conn, cancellationToken).ConfigureAwait(false);

		var where = new List<string>();
		if (missingDetailsOnly)
		{
			where.Add("details_fetched_at_utc IS NULL");
		}
		var hasQuery = !string.IsNullOrWhiteSpace(q);
		if (hasQuery)
		{
			where.Add("title LIKE $q");
		}
		var whereSql = where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);

		var cmd = conn.CreateCommand();
		cmd.CommandText = $"""
			SELECT tmdb_id, title, release_year, poster_path, backdrop_path,
				details_fetched_at_utc, updated_at_utc
			FROM tmdb_movies
			{whereSql}
			ORDER BY updated_at_utc DESC
			LIMIT $take OFFSET $skip;
		""";
		cmd.Parameters.AddWithValue("$take", take);
		cmd.Parameters.AddWithValue("$skip", skip);
		if (hasQuery)
		{
			cmd.Parameters.AddWithValue("$q", "%" + q + "%");
		}

		var results = new List<TmdbStoredMovie>(capacity: take);
		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var id = reader.GetInt32(0);
			var title = reader.IsDBNull(1) ? $"TMDB:{id}" : reader.GetString(1);
			int? year = reader.IsDBNull(2) ? null : reader.GetInt32(2);
			var posterPath = reader.IsDBNull(3) ? null : reader.GetString(3);
			var backdropPath = reader.IsDBNull(4) ? null : reader.GetString(4);
			var detailsFetchedRaw = reader.IsDBNull(5) ? null : reader.GetString(5);
			var updatedRaw = reader.IsDBNull(6) ? null : reader.GetString(6);

			DateTimeOffset? detailsFetchedAt = null;
			if (!string.IsNullOrWhiteSpace(detailsFetchedRaw) && DateTimeOffset.TryParse(detailsFetchedRaw, out var parsedDetails))
			{
				detailsFetchedAt = parsedDetails;
			}

			DateTimeOffset? updatedAt = null;
			if (!string.IsNullOrWhiteSpace(updatedRaw) && DateTimeOffset.TryParse(updatedRaw, out var parsedUpdated))
			{
				updatedAt = parsedUpdated;
			}

			results.Add(new TmdbStoredMovie(
				TmdbId: id,
				Title: title,
				ReleaseYear: year,
				PosterPath: posterPath,
				BackdropPath: backdropPath,
				DetailsFetchedAtUtc: detailsFetchedAt,
				UpdatedAtUtc: updatedAt));
		}

		return results;
	}

	private static int? TryParseYear(string? releaseDate)
	{
		if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
		{
			return null;
		}

		return int.TryParse(releaseDate.AsSpan(0, 4), out var year) ? year : null;
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
			now = DateTimeOffset.UtcNow;
			if (now - _lastMaintenanceUtc < MaintenanceInterval)
			{
				return;
			}

			try
			{
				var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
				var maxMovies = settings.MaxMovies;

				var trimMovies = conn.CreateCommand();
				trimMovies.CommandText = """
					DELETE FROM tmdb_movies
					WHERE rowid IN (
						SELECT rowid
						FROM tmdb_movies
						ORDER BY updated_at_utc DESC
						LIMIT -1 OFFSET $maxMovies
					);
				""";
				trimMovies.Parameters.AddWithValue("$maxMovies", maxMovies);
				await ExecuteNonQueryWithRetryAsync(trimMovies, cancellationToken).ConfigureAwait(false);
 			}
 			catch
 			{
 				// best-effort
 			}
 
 			_lastMaintenanceUtc = now;
 		}
 		finally
 		{
 			_maintenanceLock.Release();
 		}
 	}
}
