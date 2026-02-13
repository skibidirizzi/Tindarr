using Tindarr.Domain.Common;

namespace Tindarr.Application.Abstractions.Persistence;

public interface IPlexLibraryCacheRepository
{
	Task<IReadOnlyCollection<int>> GetTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task ReplaceTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);

	Task AddTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);

	Task RemoveTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken);
}
