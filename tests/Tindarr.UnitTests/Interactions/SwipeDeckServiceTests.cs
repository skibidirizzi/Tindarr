using Tindarr.Application.Features.Interactions;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.UnitTests.Interactions;

public sealed class SwipeDeckServiceTests
{
	[Fact]
	public async Task GetDeckAsync_FiltersInteractedTmdbIds()
	{
		var scope = ServiceScope.TryCreate("plex", "server1", out var s) ? s! : throw new Exception("scope");

		var source = new StubSource([
			new SwipeCard(1, "A", null, null, null, null, null),
			new SwipeCard(2, "B", null, null, null, null, null),
			new SwipeCard(3, "C", null, null, null, null, null),
			new SwipeCard(4, "D", null, null, null, null, null)
		]);

		var store = new StubStore(interacted: [2, 3]);
		var svc = new SwipeDeckService(store, source);

		var deck = await svc.GetDeckAsync("u1", scope, limit: 10, CancellationToken.None);

		Assert.Equal([1, 4], deck.Select(x => x.TmdbId).ToArray());
	}

	private sealed class StubSource(IReadOnlyList<SwipeCard> cards) : ISwipeDeckSource
	{
		public Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
		{
			return Task.FromResult(cards);
		}
	}

	private sealed class StubStore(IReadOnlyCollection<int> interacted) : IInteractionStore
	{
		public Task AddAsync(Interaction interaction, CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task<IReadOnlyCollection<int>> GetInteractedTmdbIdsAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
		{
			return Task.FromResult(interacted);
		}

		public Task<IReadOnlyList<Interaction>> ListAsync(string userId, ServiceScope scope, InteractionAction? action, int? tmdbId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task<IReadOnlyList<Interaction>> ListForScopeAsync(ServiceScope scope, int? tmdbId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
	}
}

