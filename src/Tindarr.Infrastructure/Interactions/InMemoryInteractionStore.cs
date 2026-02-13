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

            // Delete the most recent interaction (by CreatedAtUtc), deterministic on ties.
            var mostRecentIndex = 0;
            var mostRecentTime = list[0].CreatedAtUtc;

            for (var i = 1; i < list.Count; i++)
            {
                var t = list[i].CreatedAtUtc;
                if (t > mostRecentTime || (t == mostRecentTime && i > mostRecentIndex))
                {
                    mostRecentTime = t;
                    mostRecentIndex = i;
                }
            }

            var removed = list[mostRecentIndex];
            list.RemoveAt(mostRecentIndex);
            return Task.FromResult<Interaction?>(removed);
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

    public Task<IReadOnlyList<Interaction>> ListAsync(
        string userId,
        ServiceScope scope,
        InteractionAction? action,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(userId, scope);
        if (!_interactions.TryGetValue(key, out var list))
        {
            return Task.FromResult<IReadOnlyList<Interaction>>(Array.Empty<Interaction>());
        }

        lock (list)
        {
            IEnumerable<Interaction> filtered = list;

            if (action is not null)
            {
                filtered = filtered.Where(x => x.Action == action.Value);
            }

            if (tmdbId is not null)
            {
                filtered = filtered.Where(x => x.TmdbId == tmdbId.Value);
            }

            var result = filtered
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(Math.Max(1, limit))
                .ToList();

            return Task.FromResult<IReadOnlyList<Interaction>>(result);
        }
    }

    private static string BuildKey(string userId, ServiceScope scope)
    {
        return $"{userId}:{scope.ServiceType}:{scope.ServerId}";
    }

    public Task<IReadOnlyList<Interaction>> ListForScopeAsync(
        ServiceScope scope,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken)
    {
        var suffix = $":{scope.ServiceType}:{scope.ServerId}";

        var all = new List<Interaction>();
        foreach (var kvp in _interactions)
        {
            if (!kvp.Key.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var list = kvp.Value;
            lock (list)
            {
                all.AddRange(list);
            }
        }

        IEnumerable<Interaction> filtered = all;
        if (tmdbId is not null)
        {
            filtered = filtered.Where(x => x.TmdbId == tmdbId.Value);
        }

        var result = filtered
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToList();

        return Task.FromResult<IReadOnlyList<Interaction>>(result);
    }

    public Task<IReadOnlyList<Interaction>> SearchAsync(
        string? userId,
        ServiceScope? scope,
        InteractionAction? action,
        int? tmdbId,
        int limit,
        CancellationToken cancellationToken)
    {
        var all = new List<Interaction>();
        foreach (var kvp in _interactions)
        {
            var list = kvp.Value;
            lock (list)
            {
                all.AddRange(list);
            }
        }

        IEnumerable<Interaction> filtered = all;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            filtered = filtered.Where(x => x.UserId == userId);
        }

        if (scope is not null)
        {
            filtered = filtered.Where(x => x.Scope.ServiceType == scope.ServiceType && x.Scope.ServerId == scope.ServerId);
        }

        if (action is not null)
        {
            filtered = filtered.Where(x => x.Action == action.Value);
        }

        if (tmdbId is not null)
        {
            filtered = filtered.Where(x => x.TmdbId == tmdbId.Value);
        }

        var result = filtered
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToList();

        return Task.FromResult<IReadOnlyList<Interaction>>(result);
    }
}
