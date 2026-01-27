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
}

