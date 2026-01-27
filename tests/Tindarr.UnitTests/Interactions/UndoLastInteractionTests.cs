using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Interactions;

namespace Tindarr.UnitTests.Interactions;

public sealed class UndoLastInteractionTests
{
	[Fact]
	public async Task Undo_deletes_most_recent_by_timestamp_per_user_and_scope()
	{
		var store = new InMemoryInteractionStore();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		// Add out of order (by CreatedAtUtc)
		await store.AddAsync(new Interaction("u1", scope, 1, InteractionAction.Like, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
		await store.AddAsync(new Interaction("u1", scope, 2, InteractionAction.Nope, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
		await store.AddAsync(new Interaction("u1", scope, 3, InteractionAction.Skip, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);

		var undone = await store.UndoLastAsync("u1", scope, CancellationToken.None);

		Assert.NotNull(undone);
		Assert.Equal(2, undone!.TmdbId);

		var remaining = await store.ListAsync("u1", scope, action: null, tmdbId: null, limit: 10, CancellationToken.None);
		Assert.DoesNotContain(remaining, x => x.TmdbId == 2);
		Assert.Contains(remaining, x => x.TmdbId == 1);
		Assert.Contains(remaining, x => x.TmdbId == 3);
	}

	[Fact]
	public async Task Undo_is_scoped_by_user_and_service_scope()
	{
		var store = new InMemoryInteractionStore();
		var scopeA = new ServiceScope(ServiceType.Tmdb, "tmdb");
		var scopeB = new ServiceScope(ServiceType.Tmdb, "other");

		await store.AddAsync(new Interaction("u1", scopeA, 1, InteractionAction.Like, new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
		await store.AddAsync(new Interaction("u2", scopeA, 2, InteractionAction.Like, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
		await store.AddAsync(new Interaction("u1", scopeB, 3, InteractionAction.Like, new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);

		var undone = await store.UndoLastAsync("u1", scopeA, CancellationToken.None);

		Assert.NotNull(undone);
		Assert.Equal(1, undone!.TmdbId);

		// Other user + other scope should remain untouched.
		var u2 = await store.ListAsync("u2", scopeA, action: null, tmdbId: null, limit: 10, CancellationToken.None);
		Assert.Single(u2);
		Assert.Equal(2, u2[0].TmdbId);

		var u1scopeB = await store.ListAsync("u1", scopeB, action: null, tmdbId: null, limit: 10, CancellationToken.None);
		Assert.Single(u1scopeB);
		Assert.Equal(3, u1scopeB[0].TmdbId);
	}
}

