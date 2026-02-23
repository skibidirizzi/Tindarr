using Tindarr.Domain.Common;

namespace Tindarr.Application.Abstractions.Persistence;

public interface ILibraryCacheRepository
{
	/// <summary>All distinct TmdbIds in the library cache for the given service type (any server). Used e.g. to exclude Radarr titles from the main TMDB swipe deck.</summary>
	Task<IReadOnlyCollection<int>> GetTmdbIdsForServiceTypeAsync(ServiceType serviceType, CancellationToken cancellationToken);

	Task<IReadOnlyCollection<int>> GetTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<IReadOnlyList<int>> ListTmdbIdsAsync(ServiceScope scope, int skip, int take, CancellationToken cancellationToken);

	Task<int> CountTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task ReplaceTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);

	Task AddTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);
}
