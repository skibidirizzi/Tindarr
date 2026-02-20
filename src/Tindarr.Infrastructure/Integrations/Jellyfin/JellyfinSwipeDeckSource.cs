using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Integrations.Interactions;

namespace Tindarr.Infrastructure.Integrations.Jellyfin;

public sealed class JellyfinSwipeDeckSource(
	ILibraryCacheRepository libraryCache,
	TmdbSwipeDeckCandidateBuilder candidateBuilder,
	IInteractionStore interactionStore) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var ids = await libraryCache.GetTmdbIdsAsync(scope, cancellationToken).ConfigureAwait(false);
		if (ids.Count == 0)
		{
			throw new InvalidOperationException("Jellyfin library is not synced yet. Sync the Jellyfin library in Admin Console, then try again.");
		}

		var interacted = await interactionStore.GetInteractedTmdbIdsAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		var interactedSet = interacted as HashSet<int> ?? interacted.ToHashSet();
		var candidateIds = ids
			.Where(id => id > 0 && !interactedSet.Contains(id))
			.Take(250)
			.ToList();

		return await candidateBuilder.BuildCandidatesAsync(candidateIds, "Jellyfin", cancellationToken).ConfigureAwait(false);
	}
}
