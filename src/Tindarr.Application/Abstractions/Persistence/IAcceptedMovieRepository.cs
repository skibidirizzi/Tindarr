using Tindarr.Domain.AcceptedMovies;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Abstractions.Persistence;

public interface IAcceptedMovieRepository
{
	Task<IReadOnlyList<AcceptedMovie>> ListAsync(ServiceScope scope, int limit, CancellationToken cancellationToken);

	Task<IReadOnlyList<AcceptedMovie>> ListSinceIdAsync(ServiceScope scope, long? afterId, int limit, CancellationToken cancellationToken);

	Task<bool> TryAddAsync(ServiceScope scope, int tmdbId, string? acceptedByUserId, CancellationToken cancellationToken);
}

