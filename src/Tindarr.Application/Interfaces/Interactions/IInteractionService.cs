using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Interfaces.Interactions;

public interface IInteractionService
{
    Task<Interaction> AddAsync(string userId, ServiceScope scope, int tmdbId, InteractionAction action, CancellationToken cancellationToken);
    Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<Interaction>> ListAsync(
        string userId,
        ServiceScope scope,
        InteractionAction? action,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken);
}
