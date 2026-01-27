using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Features.Interactions;

public sealed class InteractionService(IInteractionStore store) : IInteractionService
{
    public async Task<Interaction> AddAsync(string userId, ServiceScope scope, int tmdbId, InteractionAction action, CancellationToken cancellationToken)
    {
        var interaction = new Interaction(userId, scope, tmdbId, action, DateTimeOffset.UtcNow);
        await store.AddAsync(interaction, cancellationToken);
        return interaction;
    }

    public Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
    {
        return store.UndoLastAsync(userId, scope, cancellationToken);
    }
}
