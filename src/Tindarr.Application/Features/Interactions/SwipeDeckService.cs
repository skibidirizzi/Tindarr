using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Features.Interactions;

public sealed class SwipeDeckService(IInteractionStore interactionStore, ISwipeDeckSource source, ILibraryCacheRepository libraryCache) : ISwipeDeckService
{
    public async Task<IReadOnlyList<SwipeCard>> GetDeckAsync(string userId, ServiceScope scope, int limit, CancellationToken cancellationToken)
    {
        var candidates = await source.GetCandidatesAsync(userId, scope, cancellationToken);
        var interacted = await interactionStore.GetInteractedTmdbIdsAsync(userId, scope, cancellationToken);
		var libraryIds = scope.ServiceType == ServiceType.Radarr
			? await libraryCache.GetTmdbIdsAsync(scope, cancellationToken)
			: Array.Empty<int>();

        var filtered = candidates
            .Where(card => !interacted.Contains(card.TmdbId))
			.Where(card => libraryIds.Count == 0 || !libraryIds.Contains(card.TmdbId))
            .Take(Math.Max(1, limit))
            .ToList();

        return filtered;
    }
}
