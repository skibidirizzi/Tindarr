using Tindarr.Domain.Common;
using Tindarr.Application.Abstractions.Integrations;

namespace Tindarr.Application.Abstractions.Persistence;

public interface IPlexLibraryCacheRepository
{
	Task<IReadOnlyCollection<int>> GetTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<IReadOnlyList<PlexLibraryItem>> ListItemsAsync(ServiceScope scope, int skip, int take, CancellationToken cancellationToken);

	Task<string?> TryGetRatingKeyAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken);

	Task ReplaceTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);

	Task ReplaceItemsAsync(ServiceScope scope, IReadOnlyCollection<PlexLibraryItem> items, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);

	Task AddTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken);

	Task RemoveTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken);
}
