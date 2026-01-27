using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Interfaces.Interactions;

public interface IInteractionStore
{
    Task AddAsync(Interaction interaction, CancellationToken cancellationToken);
    Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<int>> GetInteractedTmdbIdsAsync(string userId, ServiceScope scope, CancellationToken cancellationToken);
}
