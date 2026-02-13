using Tindarr.Application.Abstractions.Domain;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Services;

public sealed class MatchingEngine : IMatchingEngine
{
	public IReadOnlyList<int> ComputeLikedByAllMatches(
		ServiceScope scope,
		IReadOnlyList<Interaction> interactions,
		int minUsers = 2)
	{
		minUsers = Math.Max(2, minUsers);

		if (interactions.Count == 0)
		{
			return Array.Empty<int>();
		}

		// Only consider interactions in the requested scope.
		var scoped = interactions.Where(x => x.Scope.ServiceType == scope.ServiceType && x.Scope.ServerId == scope.ServerId).ToList();
		if (scoped.Count == 0)
		{
			return Array.Empty<int>();
		}

		var users = scoped.Select(x => x.UserId).Distinct().ToArray();
		if (users.Length < minUsers)
		{
			return Array.Empty<int>();
		}

		// Reduce to "current stance" per (UserId, TmdbId) by taking the latest timestamp.
		var latest = new Dictionary<(string UserId, int TmdbId), Interaction>(capacity: scoped.Count);
		foreach (var interaction in scoped)
		{
			var key = (interaction.UserId, interaction.TmdbId);

			if (!latest.TryGetValue(key, out var existing) || interaction.CreatedAtUtc > existing.CreatedAtUtc)
			{
				latest[key] = interaction;
			}
			else if (interaction.CreatedAtUtc == existing.CreatedAtUtc)
			{
				// Deterministic tie-breaker: keep the later enumeration item.
				latest[key] = interaction;
			}
		}

		// For each movie, count how many distinct users currently Like/Superlike it.
		var likedCounts = new Dictionary<int, int>();
		foreach (var entry in latest.Values)
		{
			if (!IsPositive(entry.Action))
			{
				continue;
			}

			likedCounts.TryGetValue(entry.TmdbId, out var count);
			likedCounts[entry.TmdbId] = count + 1;
		}

		var superliked = latest.Values
			.Where(x => x.Action == InteractionAction.Superlike)
			.Select(x => x.TmdbId)
			.Distinct();

		// "Match": a movie is matched if at least minUsers distinct users currently Like/Superlike it.
		var required = minUsers;
		return likedCounts
			.Where(kvp => kvp.Value >= required)
			.Select(kvp => kvp.Key)
			.Concat(superliked)
			.Distinct()
			.OrderBy(x => x)
			.ToList();
	}

	private static bool IsPositive(InteractionAction action)
	{
		return action is InteractionAction.Like or InteractionAction.Superlike;
	}
}

