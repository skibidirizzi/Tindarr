using Tindarr.Application.Services;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.UnitTests.Matching;

public sealed class LikedByAllMatchingEngineTests
{
	[Fact]
	public void Returns_empty_when_fewer_than_two_users()
	{
		var engine = new MatchingEngine();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var interactions = new[]
		{
			new Interaction("u1", scope, 10, InteractionAction.Like, DateTimeOffset.UtcNow)
		};

		var matches = engine.ComputeLikedByAllMatches(scope, interactions, minUsers: 2);
		Assert.Empty(matches);
	}

	[Fact]
	public void Returns_empty_when_min_users_is_less_than_two()
	{
		var engine = new MatchingEngine();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var interactions = new[]
		{
			new Interaction("u1", scope, 10, InteractionAction.Like, DateTimeOffset.UtcNow)
		};

		var matches = engine.ComputeLikedByAllMatches(scope, interactions, minUsers: 1);
		Assert.Empty(matches);
	}

	[Fact]
	public void Computes_liked_by_all_using_latest_action_per_user_and_movie()
	{
		var engine = new MatchingEngine();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var interactions = new[]
		{
			// Movie 1: both like => match
			new Interaction("u1", scope, 1, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,0,TimeSpan.Zero)),
			new Interaction("u2", scope, 1, InteractionAction.Superlike, new DateTimeOffset(2025,1,1,0,0,1,TimeSpan.Zero)),

			// Movie 2: u2 nopes => no match
			new Interaction("u1", scope, 2, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,2,TimeSpan.Zero)),
			new Interaction("u2", scope, 2, InteractionAction.Nope, new DateTimeOffset(2025,1,1,0,0,3,TimeSpan.Zero)),

			// Movie 3: u2 has no stance => no match
			new Interaction("u1", scope, 3, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,4,TimeSpan.Zero)),

			// Movie 4: u1 liked but later skipped => latest is non-positive => no match
			new Interaction("u1", scope, 4, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,5,TimeSpan.Zero)),
			new Interaction("u1", scope, 4, InteractionAction.Skip, new DateTimeOffset(2025,1,1,0,0,6,TimeSpan.Zero)),
			new Interaction("u2", scope, 4, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,7,TimeSpan.Zero))
		};

		var matches = engine.ComputeLikedByAllMatches(scope, interactions, minUsers: 2);
		Assert.Equal(new[] { 1 }, matches);
	}

	[Fact]
	public void Ignores_interactions_outside_scope()
	{
		var engine = new MatchingEngine();
		var scopeA = new ServiceScope(ServiceType.Tmdb, "tmdb");
		var scopeB = new ServiceScope(ServiceType.Tmdb, "other");

		var interactions = new[]
		{
			new Interaction("u1", scopeA, 1, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,0,TimeSpan.Zero)),
			new Interaction("u2", scopeA, 1, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,1,TimeSpan.Zero)),

			// Other scope noise (should not change results)
			new Interaction("u3", scopeB, 1, InteractionAction.Nope, new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero))
		};

		var matches = engine.ComputeLikedByAllMatches(scopeA, interactions, minUsers: 2);
		Assert.Equal(new[] { 1 }, matches);
	}

	[Fact]
	public void Matches_when_at_least_two_users_like_even_if_more_users_exist()
	{
		var engine = new MatchingEngine();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var interactions = new[]
		{
			new Interaction("u1", scope, 99, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,0,TimeSpan.Zero)),
			new Interaction("u2", scope, 99, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,1,TimeSpan.Zero)),

			// Third user exists in-scope (has interacted), but did not like movie 99.
			new Interaction("u3", scope, 123, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,2,TimeSpan.Zero))
		};

		var matches = engine.ComputeLikedByAllMatches(scope, interactions, minUsers: 2);
		Assert.Equal(new[] { 99 }, matches);
	}

	[Fact]
	public void Superlikes_count_as_likes_for_matches()
	{
		var engine = new MatchingEngine();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var interactions = new[]
		{
			new Interaction("u1", scope, 77, InteractionAction.Superlike, new DateTimeOffset(2025,1,1,0,0,0,TimeSpan.Zero)),
			new Interaction("u2", scope, 77, InteractionAction.Like, new DateTimeOffset(2025,1,1,0,0,1,TimeSpan.Zero)),
		};

		var matches = engine.ComputeLikedByAllMatches(scope, interactions, minUsers: 2);
		Assert.Equal(new[] { 77 }, matches);
	}

	[Fact]
	public void Superlike_is_an_automatic_match_when_at_least_two_users_exist_in_scope()
	{
		var engine = new MatchingEngine();
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var interactions = new[]
		{
			new Interaction("u1", scope, 555, InteractionAction.Superlike, new DateTimeOffset(2025,1,1,0,0,0,TimeSpan.Zero)),

			// Ensure there are at least two distinct users in the scope.
			new Interaction("u2", scope, 123, InteractionAction.Nope, new DateTimeOffset(2025,1,1,0,0,1,TimeSpan.Zero)),
		};

		var matches = engine.ComputeLikedByAllMatches(scope, interactions, minUsers: 2);
		Assert.Equal(new[] { 555 }, matches);
	}
}

