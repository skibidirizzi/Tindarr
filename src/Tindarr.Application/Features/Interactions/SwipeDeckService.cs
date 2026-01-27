using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Features.Interactions;

public sealed class SwipeDeckService(IInteractionStore interactionStore, ISwipeDeckSource source) : ISwipeDeckService
{
    public async Task<IReadOnlyList<SwipeCard>> GetDeckAsync(string userId, ServiceScope scope, int limit, CancellationToken cancellationToken)
    {
        var candidates = await source.GetCandidatesAsync(scope, cancellationToken);
        var interacted = await interactionStore.GetInteractedTmdbIdsAsync(userId, scope, cancellationToken);

        var filtered = candidates
            .Where(card => !interacted.Contains(card.TmdbId))
            .Take(Math.Max(1, limit))
            .ToList();

        return filtered;
    }
}
