using Tindarr.Application.Abstractions.Domain;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Services;

public sealed class MatchingEngine : IMatchingEngine
{
	public IReadOnlyList<int> ComputeLikedByAllMatches(
		ServiceScope scope,
		IReadOnlyList<Interaction> interactions,
		int minUsers = 2,
		int? minUserPercent = null)
	{
		minUsers = Math.Max(0, minUsers);
		if (minUserPercent is not null && (minUserPercent.Value < 1 || minUserPercent.Value > 100))
		{
			minUserPercent = null;
		}

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
		var normalizedMinUsers = minUsers;
		if (normalizedMinUsers > 0)
		{
			normalizedMinUsers = Math.Clamp(normalizedMinUsers, 2, 50);
		}
		else if (minUserPercent is null)
		{
			normalizedMinUsers = 2;
		}

		if (users.Length < normalizedMinUsers)
		{
			return Array.Empty<int>();
		}

		int? requiredByCount = null;
		if (normalizedMinUsers > 0)
		{
			requiredByCount = normalizedMinUsers;
		}

		int? requiredByPercent = null;
		if (minUserPercent is not null)
		{
			requiredByPercent = (int)Math.Ceiling(users.Length * (minUserPercent.Value / 100.0));
			requiredByPercent = Math.Clamp(requiredByPercent.Value, 1, 50);
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

		return likedCounts
			.Where(kvp =>
				(requiredByCount is not null && kvp.Value >= requiredByCount.Value)
				|| (requiredByPercent is not null && kvp.Value >= requiredByPercent.Value))
			.Select(kvp => kvp.Key)
			.OrderBy(x => x)
			.ToList();
	}

	private static bool IsPositive(InteractionAction action)
	{
		return action is InteractionAction.Like or InteractionAction.Superlike;
	}
}

