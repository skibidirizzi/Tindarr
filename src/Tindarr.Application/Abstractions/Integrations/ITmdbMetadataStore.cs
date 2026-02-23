using Tindarr.Contracts.Movies;
using Tindarr.Contracts.Tmdb;

namespace Tindarr.Application.Abstractions.Integrations;

public enum TmdbPosterMode
{
	Tmdb = 0,
	LocalProxy = 1
}

public sealed record TmdbMetadataSettings(
	int MaxMovies,
	int MaxPoolPerUser,
	int ImageCacheMaxMb,
	TmdbPosterMode PosterMode,
	string? PrewarmOriginalLanguage,
	string? PrewarmRegion);

public sealed record TmdbMetadataStats(int MovieCount, long ImageCacheBytes);

public sealed record TmdbStoredMovie(
	int TmdbId,
	string Title,
	string? Overview,
	string? ReleaseDate,
	int? ReleaseYear,
	string? PosterPath,
	string? BackdropPath,
	string? MpaaRating,
	double? Rating,
	int? VoteCount,
	IReadOnlyList<string> Genres,
	IReadOnlyList<string> Regions,
	string? OriginalLanguage,
	int? RuntimeMinutes,
	DateTimeOffset? DetailsFetchedAtUtc,
	DateTimeOffset? UpdatedAtUtc);

public interface ITmdbMetadataStore
{
	ValueTask<TmdbMetadataSettings> GetSettingsAsync(CancellationToken cancellationToken);

	ValueTask<TmdbMetadataSettings> SetSettingsAsync(TmdbMetadataSettings settings, CancellationToken cancellationToken);

	ValueTask<TmdbMetadataStats> GetStatsAsync(CancellationToken cancellationToken);

	Task UpsertMoviesAsync(IReadOnlyList<TmdbDiscoverMovieRecord> movies, CancellationToken cancellationToken);

	Task AddToUserPoolAsync(string userId, IReadOnlyList<TmdbDiscoverMovieRecord> discovered, CancellationToken cancellationToken);

	Task ClearUserPoolAsync(string userId, CancellationToken cancellationToken);

	Task<IReadOnlyList<TmdbDiscoverMovieRecord>> GetUserPoolAsync(string userId, int limit, CancellationToken cancellationToken);

	Task<IReadOnlyList<int>> ListMoviesNeedingDetailsAsync(int limit, CancellationToken cancellationToken);

	/// <summary>Count of movies that still need details (for populate progress).</summary>
	Task<int> CountMoviesNeedingDetailsAsync(CancellationToken cancellationToken);

	Task UpdateMovieDetailsAsync(MovieDetailsDto details, CancellationToken cancellationToken);

	Task<TmdbStoredMovie?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken);

	Task<IReadOnlyList<TmdbDiscoverMovieRecord>> ListDeckCandidatesAsync(int take, CancellationToken cancellationToken);

	Task<IReadOnlyList<TmdbStoredMovie>> ListMoviesAsync(int skip, int take, bool missingDetailsOnly, string? titleQuery, CancellationToken cancellationToken);

	/// <summary>Merge tmdb_movies from an external SQLite file into the store. Deduplication by tmdb_id. Returns inserted, updated, skipped counts and any not-imported reasons.</summary>
	Task<TmdbImportResultDto> ImportFromFileAsync(string sourceDbPath, CancellationToken cancellationToken);
}
