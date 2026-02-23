using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Features.Interactions;

public sealed class SwipeDeckService(IInteractionStore interactionStore, ISwipeDeckSource source, ILibraryCacheRepository libraryCache) : ISwipeDeckService
{
	private const int MinimumRuntimeMinutes = 80;

	public async Task<IReadOnlyList<SwipeCard>> GetDeckAsync(string userId, ServiceScope scope, int limit, CancellationToken cancellationToken)
	{
		var candidates = await source.GetCandidatesAsync(userId, scope, cancellationToken);
		var interacted = await interactionStore.GetInteractedTmdbIdsAsync(userId, scope, cancellationToken);
		var interactedSet = interacted.ToHashSet();

		// Exclude only movies we know have runtime < 80 minutes; mark them as Nope so they never appear again.
		// Do not treat null runtime as short: discover often omits runtime (it comes from details), so null = unknown = keep.
		var now = DateTimeOffset.UtcNow;
		foreach (var card in candidates)
		{
			if (card.RuntimeMinutes is { } rt && rt < MinimumRuntimeMinutes && !interactedSet.Contains(card.TmdbId))
			{
				await interactionStore.AddAsync(new Interaction(userId, scope, card.TmdbId, InteractionAction.Nope, now), cancellationToken).ConfigureAwait(false);
				interactedSet.Add(card.TmdbId);
			}
		}

		// When viewing Radarr scope: exclude movies already in that Radarr library. When viewing main (TMDB) deck: exclude movies that exist in any Radarr library.
		var libraryIds = scope.ServiceType == ServiceType.Radarr
			? await libraryCache.GetTmdbIdsAsync(scope, cancellationToken)
			: Array.Empty<int>();
		var radarrIds = scope.ServiceType == ServiceType.Tmdb
			? await libraryCache.GetTmdbIdsForServiceTypeAsync(ServiceType.Radarr, cancellationToken)
			: Array.Empty<int>();

		var shouldFilterByLibrary = libraryIds.Count > 0;
		var shouldFilterByRadarr = radarrIds.Count > 0;
		HashSet<int>? libraryIdSet = shouldFilterByLibrary ? (libraryIds as HashSet<int> ?? libraryIds.ToHashSet()) : null;
		HashSet<int>? radarrIdSet = shouldFilterByRadarr ? (radarrIds as HashSet<int> ?? radarrIds.ToHashSet()) : null;

		// Include cards with runtime >= 80 or unknown (null) runtime; exclude only when we know runtime is short.
		var filtered = candidates
			.Where(card => !card.RuntimeMinutes.HasValue || card.RuntimeMinutes.Value >= MinimumRuntimeMinutes)
			.Where(card => !interactedSet.Contains(card.TmdbId))
			.Where(card => !shouldFilterByLibrary || !libraryIdSet!.Contains(card.TmdbId))
			.Where(card => !shouldFilterByRadarr || !radarrIdSet!.Contains(card.TmdbId))
			.Take(Math.Max(1, limit))
			.ToList();

		return filtered;
	}
}
