using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Domain;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Contracts.Matching;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/matches")]
public sealed class MatchesController(
	IInteractionStore interactionStore,
	IMatchingEngine matchingEngine,
	IServiceSettingsRepository settingsRepo,
	IUserRepository userRepo) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<MatchesResponse>> List(
		[FromQuery] string serviceType,
		[FromQuery] string serverId,
		[FromQuery] int minUsers = 2,
		[FromQuery] int interactionLimit = 20_000,
		CancellationToken cancellationToken = default)
	{
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope))
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var userId = User.GetUserId();

		// Solo media-server swiping: list everything you personally liked/superliked.
		// This is intentionally not persisted in the main DB (see RoutingInteractionStore).
		if (scope!.ServiceType is ServiceType.Plex or ServiceType.Jellyfin or ServiceType.Emby)
		{
			interactionLimit = Math.Clamp(interactionLimit, 1, 50_000);

			var liked = await interactionStore.ListAsync(userId, scope!, InteractionAction.Like, tmdbId: null, interactionLimit, cancellationToken);
			var superliked = await interactionStore.ListAsync(userId, scope!, InteractionAction.Superlike, tmdbId: null, interactionLimit, cancellationToken);

			var ids = liked
				.Concat(superliked)
				.OrderByDescending(x => x.CreatedAtUtc)
				.Select(x => x.TmdbId)
				.Distinct()
				.ToList();

			return Ok(new MatchesResponse(
				scope!.ServiceType.ToString().ToLowerInvariant(),
				scope.ServerId,
				ids.Select(id => new MatchDto(id, [])).ToList()));
		}

		minUsers = Math.Clamp(minUsers, 2, 50);
		interactionLimit = Math.Clamp(interactionLimit, 1, 50_000);

		var interactions = await interactionStore.ListForScopeAsync(scope!, tmdbId: null, interactionLimit, cancellationToken);

		var effectiveMinUsers = minUsers;
		int? effectiveMinUserPercent = null;
		var settings = await settingsRepo.GetAsync(scope!, cancellationToken);
		if (settings is not null)
		{
			if (settings.MatchMinUsers is not null)
			{
				// MinUsers is validated as 1..50 in AdminController; treat <=0 as legacy/invalid and fall back to defaults.
				if (settings.MatchMinUsers.Value > 0)
				{
					effectiveMinUsers = Math.Clamp(settings.MatchMinUsers.Value, 1, 50);
				}
				else if (settings.MatchMinUserPercent is null)
				{
					effectiveMinUsers = 2;
				}
				else
				{
					// Percent-only configuration disables count threshold.
					effectiveMinUsers = 0;
				}
			}
			else if (settings.MatchMinUserPercent is not null)
			{
				// If only percent is configured, disable count threshold.
				effectiveMinUsers = 0;
			}

			effectiveMinUserPercent = settings.MatchMinUserPercent;
		}

		var tmdbIds = matchingEngine.ComputeLikedByAllMatches(scope!, interactions, effectiveMinUsers, effectiveMinUserPercent);

		var matchedWithDisplayNames = await BuildMatchedWithDisplayNamesAsync(
			userId,
			scope!,
			interactions,
			tmdbIds,
			cancellationToken);

		return Ok(new MatchesResponse(
			scope!.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			tmdbIds.Select(id => new MatchDto(id, matchedWithDisplayNames.TryGetValue(id, out var names) ? names : [])).ToList()));
	}

	/// <summary>
	/// For each matched tmdbId, get display names of other users (excluding current) who liked/superliked it.
	/// </summary>
	private async Task<Dictionary<int, IReadOnlyList<string>>> BuildMatchedWithDisplayNamesAsync(
		string currentUserId,
		ServiceScope scope,
		IReadOnlyList<Interaction> interactions,
		IReadOnlyList<int> tmdbIds,
		CancellationToken cancellationToken)
	{
		var tmdbIdSet = tmdbIds.ToHashSet();
		var scoped = interactions
			.Where(x => x.Scope.ServiceType == scope.ServiceType && x.Scope.ServerId == scope.ServerId && tmdbIdSet.Contains(x.TmdbId))
			.ToList();

		var latest = new Dictionary<(string UserId, int TmdbId), Interaction>(capacity: scoped.Count);
		foreach (var interaction in scoped)
		{
			if (interaction.Action is not InteractionAction.Like and not InteractionAction.Superlike)
				continue;
			var key = (interaction.UserId, interaction.TmdbId);
			if (!latest.TryGetValue(key, out var existing) || interaction.CreatedAtUtc >= existing.CreatedAtUtc)
				latest[key] = interaction;
		}

		var userIdsByTmdbId = new Dictionary<int, List<string>>();
		foreach (var kv in latest)
		{
			var (uid, tmdbId) = kv.Key;
			if (uid == currentUserId)
				continue;
			if (!userIdsByTmdbId.TryGetValue(tmdbId, out var list))
			{
				list = [];
				userIdsByTmdbId[tmdbId] = list;
			}
			if (!list.Contains(uid))
				list.Add(uid);
		}

		var allUserIds = userIdsByTmdbId.Values.SelectMany(x => x).Distinct().ToList();
		var displayNamesByUserId = new Dictionary<string, string>(allUserIds.Count);
		foreach (var uid in allUserIds)
		{
			var user = await userRepo.FindByIdAsync(uid, cancellationToken);
			displayNamesByUserId[uid] = user?.DisplayName ?? uid;
		}

		var result = new Dictionary<int, IReadOnlyList<string>>(tmdbIds.Count);
		foreach (var tmdbId in tmdbIds)
		{
			IReadOnlyList<string> names = userIdsByTmdbId.TryGetValue(tmdbId, out var uids)
				? uids.Select(u => displayNamesByUserId.TryGetValue(u, out var n) ? n : u).ToList()
				: [];
			result[tmdbId] = names;
		}
		return result;
	}
}
