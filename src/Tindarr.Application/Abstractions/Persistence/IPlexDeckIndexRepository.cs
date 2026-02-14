using Tindarr.Contracts.Movies;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Abstractions.Persistence;

public interface IPlexDeckIndexRepository
{
	Task UpsertAsync(ServiceScope scope, IReadOnlyCollection<MovieDetailsDto> details, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken);

	Task<IReadOnlyList<SwipeCard>> QueryAsync(ServiceScope scope, UserPreferencesRecord preferences, int limit, CancellationToken cancellationToken);
}
