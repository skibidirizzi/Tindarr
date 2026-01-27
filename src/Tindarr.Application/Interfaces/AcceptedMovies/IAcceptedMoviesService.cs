using Tindarr.Domain.AcceptedMovies;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Interfaces.AcceptedMovies;

public interface IAcceptedMoviesService
{
	Task<IReadOnlyList<AcceptedMovie>> ListAsync(ServiceScope scope, int limit, CancellationToken cancellationToken);
	Task<bool> ForceAcceptAsync(string curatorUserId, ServiceScope scope, int tmdbId, CancellationToken cancellationToken);
}

