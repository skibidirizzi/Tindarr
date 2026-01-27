using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Abstractions.Integrations;

public interface ITmdbClient
{
	Task<IReadOnlyList<SwipeCard>> DiscoverAsync(UserPreferencesRecord preferences, int page, int limit, CancellationToken cancellationToken);

	Task<MovieDetailsDto?> GetMovieDetailsAsync(int tmdbId, CancellationToken cancellationToken);
}

