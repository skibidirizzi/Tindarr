using Tindarr.Contracts.Movies;

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
	TmdbPosterMode PosterMode);

public sealed record TmdbMetadataStats(int MovieCount, long ImageCacheBytes);

public sealed record TmdbStoredMovie(
	int TmdbId,
	string Title,
	int? ReleaseYear,
	string? PosterPath,
	string? BackdropPath,
	DateTimeOffset? DetailsFetchedAtUtc,
	DateTimeOffset? UpdatedAtUtc);

public interface ITmdbMetadataStore
{
	ValueTask<TmdbMetadataSettings> GetSettingsAsync(CancellationToken cancellationToken);

	ValueTask<TmdbMetadataSettings> SetSettingsAsync(TmdbMetadataSettings settings, CancellationToken cancellationToken);

	ValueTask<TmdbMetadataStats> GetStatsAsync(CancellationToken cancellationToken);

	Task AddToUserPoolAsync(string userId, IReadOnlyList<TmdbDiscoverMovieRecord> discovered, CancellationToken cancellationToken);

	Task ClearUserPoolAsync(string userId, CancellationToken cancellationToken);

	Task<IReadOnlyList<TmdbDiscoverMovieRecord>> GetUserPoolAsync(string userId, int limit, CancellationToken cancellationToken);

	Task<IReadOnlyList<int>> ListMoviesNeedingDetailsAsync(int limit, CancellationToken cancellationToken);

	Task UpdateMovieDetailsAsync(MovieDetailsDto details, CancellationToken cancellationToken);

	Task<TmdbStoredMovie?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken);

	Task<IReadOnlyList<TmdbStoredMovie>> ListMoviesAsync(int skip, int take, bool missingDetailsOnly, string? titleQuery, CancellationToken cancellationToken);
}
