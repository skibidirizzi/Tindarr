using Tindarr.Application.Abstractions.Domain;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Domain.Rooms;

namespace Tindarr.Application.Features.Rooms;

public sealed class RoomService(
	IRoomStore rooms,
	IRoomInteractionStore interactions,
	IMatchingEngine matchingEngine,
	ISwipeDeckSource source,
	ILibraryCacheRepository libraryCache) : IRoomService
{
	public async Task<RoomState> CreateAsync(string ownerUserId, ServiceScope scope, string? roomName, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		string roomId;
		if (!string.IsNullOrWhiteSpace(roomName))
		{
			var slug = NormalizeRoomNameToSlug(roomName);
			if (string.IsNullOrEmpty(slug))
				throw new ArgumentException("Room name must contain at least one letter or digit after normalization.");
			if (slug.Length < 3)
				throw new ArgumentException("Room name must be at least 3 characters after normalization.");
			if (slug.Length > 63)
				throw new ArgumentException("Room name must be at most 63 characters.");
			var existing = await rooms.GetAsync(slug, cancellationToken);
			if (existing is not null)
				throw new InvalidOperationException("Room name already in use.");
			roomId = slug;
		}
		else
		{
			roomId = Guid.NewGuid().ToString("N");
		}

		var state = new RoomState(
			RoomId: roomId,
			OwnerUserId: ownerUserId,
			Scope: scope,
			IsClosed: false,
			CreatedAtUtc: now,
			LastActivityAtUtc: now,
			Members: new List<RoomMember> { new(ownerUserId, now) });

		await rooms.CreateAsync(state, cancellationToken);
		return state;
	}

	/// <summary>Normalizes a display name to a URL-safe slug: lowercase, alphanumeric and hyphens only.</summary>
	private static string NormalizeRoomNameToSlug(string roomName)
	{
		var s = roomName.Trim();
		if (s.Length == 0) return string.Empty;
		var sb = new System.Text.StringBuilder(s.Length);
		var lastWasHyphen = false;
		foreach (var c in s.ToLowerInvariant())
		{
			if (char.IsLetterOrDigit(c))
			{
				sb.Append(c);
				lastWasHyphen = false;
			}
			else if (c is ' ' or '-' or '_' && !lastWasHyphen)
			{
				sb.Append('-');
				lastWasHyphen = true;
			}
		}
		// Trim leading/trailing hyphens
		var result = sb.ToString().Trim('-');
		return result;
	}

	public async Task<RoomState> JoinAsync(string roomId, string userId, CancellationToken cancellationToken)
	{
		var existing = await rooms.GetAsync(roomId, cancellationToken);
		if (existing is null)
		{
			throw new InvalidOperationException("Room not found.");
		}

		var isMember = existing.Members.Any(m => string.Equals(m.UserId, userId, StringComparison.Ordinal));
		if (existing.IsClosed && !isMember)
		{
			throw new InvalidOperationException("Room is closed to new users.");
		}

		var now = DateTimeOffset.UtcNow;
		var members = existing.Members.ToList();
		if (!isMember)
		{
			members.Add(new RoomMember(userId, now));
		}

		var updated = existing with
		{
			Members = members,
			LastActivityAtUtc = now
		};

		await rooms.UpdateAsync(updated, cancellationToken);
		return updated;
	}

	public async Task<RoomState> CloseAsync(string roomId, string ownerUserId, CancellationToken cancellationToken)
	{
		var existing = await rooms.GetAsync(roomId, cancellationToken);
		if (existing is null)
		{
			throw new InvalidOperationException("Room not found.");
		}

		if (!string.Equals(existing.OwnerUserId, ownerUserId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Only the room owner can close the room.");
		}

		if (existing.IsClosed)
		{
			return existing;
		}

		var now = DateTimeOffset.UtcNow;
		var updated = existing with
		{
			IsClosed = true,
			LastActivityAtUtc = now
		};

		await rooms.UpdateAsync(updated, cancellationToken);
		return updated;
	}

	public Task<RoomState?> GetAsync(string roomId, CancellationToken cancellationToken)
	{
		return rooms.GetAsync(roomId, cancellationToken);
	}

	public Task<IReadOnlyList<RoomState>> ListAsync(bool openOnly, CancellationToken cancellationToken)
	{
		return rooms.ListAliveAsync(openOnly, cancellationToken);
	}

	public async Task<IReadOnlyList<SwipeCard>> GetSwipeDeckAsync(
		string roomId,
		string userId,
		int limit,
		CancellationToken cancellationToken)
	{
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			throw new InvalidOperationException("Room not found.");
		}

		if (!room.Members.Any(m => string.Equals(m.UserId, userId, StringComparison.Ordinal)))
		{
			throw new InvalidOperationException("User is not a member of this room.");
		}

		if (!room.IsClosed)
		{
			throw new InvalidOperationException("Swipe deck is not available until the room is closed to new users.");
		}

		const int minimumRuntimeMinutes = 80;

		var candidates = await source.GetCandidatesAsync(userId, room.Scope, cancellationToken);

		var roomInteractions = await interactions.ListAsync(roomId, limit: 50_000, cancellationToken);
		var interacted = roomInteractions
			.Where(x => string.Equals(x.UserId, userId, StringComparison.Ordinal))
			.Select(x => x.TmdbId)
			.ToHashSet();

		// Exclude only movies we know have runtime < 80 minutes; null runtime = unknown = keep.
		var now = DateTimeOffset.UtcNow;
		foreach (var card in candidates)
		{
			if (card.RuntimeMinutes is { } rt && rt < minimumRuntimeMinutes && !interacted.Contains(card.TmdbId))
			{
				await interactions.AddAsync(roomId, new Interaction(userId, room.Scope, card.TmdbId, InteractionAction.Nope, now), cancellationToken).ConfigureAwait(false);
				interacted.Add(card.TmdbId);
			}
		}

		var libraryIds = room.Scope.ServiceType == ServiceType.Radarr
			? await libraryCache.GetTmdbIdsAsync(room.Scope, cancellationToken)
			: Array.Empty<int>();

		var shouldFilterByLibrary = libraryIds.Count > 0;
		HashSet<int>? libraryIdSet = null;
		if (shouldFilterByLibrary)
		{
			libraryIdSet = libraryIds as HashSet<int> ?? libraryIds.ToHashSet();
		}

		var filtered = candidates
			.Where(card => !card.RuntimeMinutes.HasValue || card.RuntimeMinutes.Value >= minimumRuntimeMinutes)
			.Where(card => !interacted.Contains(card.TmdbId))
			.Where(card => !shouldFilterByLibrary || !libraryIdSet!.Contains(card.TmdbId))
			.Take(Math.Max(1, Math.Clamp(limit, 1, 50)))
			.ToList();

		return filtered;
	}

	public async Task<Interaction> AddInteractionAsync(
		string roomId,
		string userId,
		int tmdbId,
		InteractionAction action,
		CancellationToken cancellationToken)
	{
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			throw new InvalidOperationException("Room not found.");
		}

		if (!room.Members.Any(m => string.Equals(m.UserId, userId, StringComparison.Ordinal)))
		{
			throw new InvalidOperationException("User is not a member of this room.");
		}

		var now = DateTimeOffset.UtcNow;
		var interaction = new Interaction(userId, room.Scope, tmdbId, action, now);
		await interactions.AddAsync(roomId, interaction, cancellationToken);

		await rooms.UpdateAsync(room with { LastActivityAtUtc = now }, cancellationToken);
		return interaction;
	}

	public async Task<IReadOnlyList<int>> ListMatchesAsync(string roomId, CancellationToken cancellationToken)
	{
		var room = await rooms.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			throw new InvalidOperationException("Room not found.");
		}

		if (!room.IsClosed)
		{
			throw new InvalidOperationException("Matches are not available until the room is closed to new users.");
		}

		var list = await interactions.ListAsync(roomId, limit: 50_000, cancellationToken);
		var minUsers = Math.Clamp(room.Members.Count, 1, 50);
		return matchingEngine.ComputeLikedByAllMatches(room.Scope, list, minUsers, minUserPercent: null);
	}
}
