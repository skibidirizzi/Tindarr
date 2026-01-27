using System.Collections.Concurrent;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Interactions;

public sealed class InMemoryInteractionStore : IInteractionStore
{
    private readonly ConcurrentDictionary<string, List<Interaction>> _interactions = new();

    public Task AddAsync(Interaction interaction, CancellationToken cancellationToken)
    {
        var key = BuildKey(interaction.UserId, interaction.Scope);
        var list = _interactions.GetOrAdd(key, _ => new List<Interaction>());
        lock (list)
        {
            list.Add(interaction);
        }

        return Task.CompletedTask;
    }

    public Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
    {
        var key = BuildKey(userId, scope);
        if (!_interactions.TryGetValue(key, out var list))
        {
            return Task.FromResult<Interaction?>(null);
        }

        lock (list)
        {
            if (list.Count == 0)
            {
                return Task.FromResult<Interaction?>(null);
            }

            var last = list[^1];
            list.RemoveAt(list.Count - 1);
            return Task.FromResult<Interaction?>(last);
        }
    }

    public Task<IReadOnlyCollection<int>> GetInteractedTmdbIdsAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
    {
        var key = BuildKey(userId, scope);
        if (!_interactions.TryGetValue(key, out var list))
        {
            return Task.FromResult<IReadOnlyCollection<int>>(Array.Empty<int>());
        }

        lock (list)
        {
            return Task.FromResult<IReadOnlyCollection<int>>(list.Select(interaction => interaction.TmdbId).ToHashSet());
        }
    }

    private static string BuildKey(string userId, ServiceScope scope)
    {
        return $"{userId}:{scope.ServiceType}:{scope.ServerId}";
    }
}
