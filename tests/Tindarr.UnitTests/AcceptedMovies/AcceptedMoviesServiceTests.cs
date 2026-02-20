using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Features.AcceptedMovies;
using Tindarr.Domain.AcceptedMovies;
using Tindarr.Domain.Common;

namespace Tindarr.UnitTests.AcceptedMovies;

public sealed class AcceptedMoviesServiceTests
{
	[Fact]
	public async Task ForceAccept_adds_movie_and_is_idempotent()
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var first = await service.ForceAcceptAsync("curator-1", scope, 123, CancellationToken.None);
		var second = await service.ForceAcceptAsync("curator-1", scope, 123, CancellationToken.None);

		Assert.True(first);
		Assert.False(second);

		var list = await service.ListAsync(scope, limit: 50, CancellationToken.None);
		Assert.Single(list);
		Assert.Equal(123, list[0].TmdbId);
		Assert.Equal("curator-1", list[0].AcceptedByUserId);
	}

	[Fact]
	public async Task ListAsync_enforces_limit_and_orders_most_recent_first()
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		foreach (var movieId in Enumerable.Range(1, 5))
		{
			await service.ForceAcceptAsync("curator-1", scope, movieId, CancellationToken.None);
		}

		var list = await service.ListAsync(scope, limit: 3, CancellationToken.None);

		Assert.Equal(3, list.Count);
		Assert.Equal(new[] { 5, 4, 3 }, list.Select(m => m.TmdbId).ToArray());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task ForceAcceptAsync_throws_for_invalid_curator_user_id(string? curatorUserId)
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		await Assert.ThrowsAsync<ArgumentException>(() =>
			service.ForceAcceptAsync(curatorUserId!, scope, 123, CancellationToken.None));
	}

	private sealed class FakeAcceptedMovieRepository : IAcceptedMovieRepository
	{
		private readonly Dictionary<(ServiceType ServiceType, string ServerId, int TmdbId), AcceptedMovie> _store = new();
		private long _nextId = 1;

		public Task<IReadOnlyList<AcceptedMovie>> ListAsync(ServiceScope scope, int limit, CancellationToken cancellationToken)
		{
			var items = _store.Values
				.Where(x => x.Scope.ServiceType == scope.ServiceType && x.Scope.ServerId == scope.ServerId)
				.OrderByDescending(x => x.AcceptedAtUtc)
				.Take(Math.Clamp(limit, 1, 500))
				.ToList();

			return Task.FromResult<IReadOnlyList<AcceptedMovie>>(items);
		}

		public Task<IReadOnlyList<AcceptedMovie>> ListSinceIdAsync(ServiceScope scope, long? afterId, int limit, CancellationToken cancellationToken)
		{
			var minId = afterId ?? 0;
			var items = _store.Values
				.Where(x => x.Scope.ServiceType == scope.ServiceType && x.Scope.ServerId == scope.ServerId && x.Id > minId)
				.OrderBy(x => x.Id)
				.Take(Math.Clamp(limit, 1, 500))
				.ToList();

			return Task.FromResult<IReadOnlyList<AcceptedMovie>>(items);
		}

		public Task<bool> TryAddAsync(ServiceScope scope, int tmdbId, string? acceptedByUserId, CancellationToken cancellationToken)
		{
			var key = (scope.ServiceType, scope.ServerId, tmdbId);
			if (_store.ContainsKey(key))
			{
				return Task.FromResult(false);
			}

			_store[key] = new AcceptedMovie(_nextId++, scope, tmdbId, acceptedByUserId, DateTimeOffset.UtcNow);
			return Task.FromResult(true);
		}
	}
}

