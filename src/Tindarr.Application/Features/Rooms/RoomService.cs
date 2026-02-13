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
	public async Task<RoomState> CreateAsync(string ownerUserId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var roomId = Guid.NewGuid().ToString("N");
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

		var candidates = await source.GetCandidatesAsync(userId, room.Scope, cancellationToken);

		var roomInteractions = await interactions.ListAsync(roomId, limit: 50_000, cancellationToken);
		var interacted = roomInteractions
			.Where(x => string.Equals(x.UserId, userId, StringComparison.Ordinal))
			.Select(x => x.TmdbId)
			.ToHashSet();

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

		var list = await interactions.ListAsync(roomId, limit: 50_000, cancellationToken);
		var minUsers = Math.Clamp(room.Members.Count, 2, 50);
		return matchingEngine.ComputeLikedByAllMatches(room.Scope, list, minUsers);
	}
}
