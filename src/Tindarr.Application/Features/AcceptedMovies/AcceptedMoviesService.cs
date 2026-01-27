using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.AcceptedMovies;
using Tindarr.Domain.AcceptedMovies;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Features.AcceptedMovies;

public sealed class AcceptedMoviesService(IAcceptedMovieRepository repo) : IAcceptedMoviesService
{
	public Task<IReadOnlyList<AcceptedMovie>> ListAsync(ServiceScope scope, int limit, CancellationToken cancellationToken)
	{
		return repo.ListAsync(scope, Math.Clamp(limit, 1, 500), cancellationToken);
	}

	public Task<bool> ForceAcceptAsync(string curatorUserId, ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(curatorUserId))
		{
			throw new ArgumentException("UserId is required.", nameof(curatorUserId));
		}

		return repo.TryAddAsync(scope, tmdbId, curatorUserId, cancellationToken);
	}
}

