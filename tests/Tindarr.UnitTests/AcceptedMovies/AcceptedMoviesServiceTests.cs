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

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task ForceAcceptAsync_ThrowsArgumentException_WhenCuratorUserIdIsNullOrWhitespace(string? curatorUserId)
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");

		var ex = await Assert.ThrowsAsync<ArgumentException>(
			() => service.ForceAcceptAsync(curatorUserId!, scope, 123, CancellationToken.None));

		Assert.Contains("UserId is required.", ex.Message);
		Assert.Equal("curatorUserId", ex.ParamName);
	}

	[Fact]
	public async Task ListAsync_ClampsLimitTo1_WhenLimitIsZero()
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");
		await service.ForceAcceptAsync("c1", scope, 1, CancellationToken.None);
		await service.ForceAcceptAsync("c1", scope, 2, CancellationToken.None);

		var list = await service.ListAsync(scope, limit: 0, CancellationToken.None);

		Assert.NotNull(list);
		Assert.Single(list);
	}

	[Fact]
	public async Task ListAsync_ClampsLimitTo500_WhenLimitExceeds500()
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");
		for (var i = 0; i < 600; i++)
		{
			await service.ForceAcceptAsync("c1", scope, 1000 + i, CancellationToken.None);
		}

		var list = await service.ListAsync(scope, limit: 10000, CancellationToken.None);

		Assert.NotNull(list);
		Assert.Equal(500, list.Count);
	}

	[Fact]
	public async Task ListAsync_ReturnsResultsOrderedByAcceptedAtDescending()
	{
		var repo = new FakeAcceptedMovieRepository();
		var service = new AcceptedMoviesService(repo);
		var scope = new ServiceScope(ServiceType.Tmdb, "tmdb");
		await service.ForceAcceptAsync("c1", scope, 101, CancellationToken.None);
		await service.ForceAcceptAsync("c1", scope, 102, CancellationToken.None);
		await service.ForceAcceptAsync("c1", scope, 103, CancellationToken.None);

		var list = await service.ListAsync(scope, limit: 10, CancellationToken.None);

		Assert.Equal(3, list.Count);
		Assert.Equal(103, list[0].TmdbId);
		Assert.Equal(102, list[1].TmdbId);
		Assert.Equal(101, list[2].TmdbId);
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

