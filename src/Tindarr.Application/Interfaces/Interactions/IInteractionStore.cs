using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Interfaces.Interactions;

public interface IInteractionStore
{
    Task AddAsync(Interaction interaction, CancellationToken cancellationToken);
    Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<int>> GetInteractedTmdbIdsAsync(string userId, ServiceScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<Interaction>> ListAsync(
        string userId,
        ServiceScope scope,
        InteractionAction? action,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Interaction>> ListForScopeAsync(
        ServiceScope scope,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Interaction>> SearchAsync(
        string? userId,
        ServiceScope? scope,
        InteractionAction? action,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken);
}
